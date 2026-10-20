using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectTracker.Web.Services;

/// <summary>Outcome of a notification attempt; failures never throw, they are reported.</summary>
public sealed record NotificationResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("reason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    [property: JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null,
    [property: JsonIgnore] string? Detail = null)
{
    // Note: Detail هو السبب التقني للفشل (يُحفظ في سجل البريد لمدير النظام فقط ولا يُعاد للمتصفح أبدًا؛ لا يحوي أسرارًا).
    public static NotificationResult Success { get; } = new(true);
    public static NotificationResult NotConfigured(string message) => new(false, "not_configured", message);
    public static NotificationResult SendFailed(string message, string? detail = null) => new(false, "send_failed", message, detail);
}

public sealed record MailRequest(string[] MailTo, string MailSubject, string MailBody, string[]? MailCc = null, string? MailFrom = null, string? DisplayName = null);

public interface IMailService
{
    Task<NotificationResult> SendAsync(MailRequest request, CancellationToken ct = default);
}

public interface ISmsService
{
    /// <summary>false ⇒ no provider: the sign-in code (OTP) is skipped instead of locking everyone out.</summary>
    bool IsConfigured { get; }

    Task<NotificationResult> SendAsync(string mobile, string message, CancellationToken ct = default);
}

/// <summary>SMTP relay (primary, then optional fallback host).</summary>
public sealed class SmtpMailService(AppConfig config, ILogger<SmtpMailService> logger, IServiceScopeFactory scopeFactory) : IMailService
{
    private const int TimeoutMs = 15_000;

    public string ToAddress(string userOrEmail)
    {
        var domain = config.MailDomain;
        var local = userOrEmail.Trim();
        var suffix = "@" + domain;
        if (local.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) local = local[..^suffix.Length];
        return $"{local}@{domain}";
    }

    /// <summary>Every send attempt (any caller) lands in the admin-visible audit trail, port of the "email sending log" requirement.</summary>
    public async Task<NotificationResult> SendAsync(MailRequest request, CancellationToken ct = default)
    {
        var result = await SendCoreAsync(request, ct);
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuditService>().WriteAsync(
            null, result.Ok ? "send" : "send_failed", "email_notification", null, null,
            new { to = request.MailTo, subject = request.MailSubject, error = result.Ok ? null : result.Message ?? result.Reason });
        return result;
    }

    private async Task<NotificationResult> SendCoreAsync(MailRequest request, CancellationToken ct)
    {
        if (!config.EmailEnabled)
            return NotificationResult.NotConfigured("إرسال البريد الإلكتروني معطّل (Email:Enabled).");
        if (string.IsNullOrEmpty(config.SmtpHost) || request.MailTo.Length == 0)
            return NotificationResult.NotConfigured("خادم البريد أو المستلم غير مضبوط.");

        // Note: مصادقة Windows الافتراضية لهوية التطبيق (UseDefaultCredentials) عند عدم ضبط مستخدم، وSTARTTLS حسب Email:EnableSsl.
        using var message = new MailMessage
        {
            From = new MailAddress(request.MailFrom is null && config.MailFromEmail is { } fullAddress ? fullAddress : ToAddress(request.MailFrom ?? config.MailFrom),
                request.DisplayName ?? config.MailDisplayName, Encoding.UTF8),
            Subject = request.MailSubject,
            SubjectEncoding = Encoding.UTF8,
            Body = request.MailBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
            DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure,
        };
        foreach (var to in request.MailTo) message.To.Add(to.Trim());
        foreach (var cc in request.MailCc ?? []) message.CC.Add(cc.Trim());

        var primaryError = await TrySendAsync(config.SmtpHost, message, ct);
        if (primaryError is null) return NotificationResult.Success;

        if (!string.IsNullOrEmpty(config.SmtpFallbackHost))
        {
            var fallbackError = await TrySendAsync(config.SmtpFallbackHost, message, ct);
            if (fallbackError is null) return NotificationResult.Success;
            logger.LogError("Both the primary and fallback mail servers failed. Primary: {Primary}; fallback: {Fallback}", primaryError, fallbackError);
            return NotificationResult.SendFailed("فشل إرسال البريد من الخادمين الأساسي والاحتياطي.", $"primary: {primaryError}; fallback: {fallbackError}");
        }

        logger.LogError("The primary mail server failed: {Error}", primaryError);
        return NotificationResult.SendFailed("فشل إرسال البريد الإلكتروني.", primaryError);
    }

    private async Task<string?> TrySendAsync(string host, MailMessage message, CancellationToken ct)
    {
        try
        {
            // NOSONAR S5332 — TLS is switched on right below when Smtp RequireTls is set; internal relays on a trusted network may not offer it.
            using var client = new SmtpClient(host, config.SmtpPort) // NOSONAR
            {
                Timeout = TimeoutMs,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };
            if (config.SmtpRequireTls) client.EnableSsl = true;
            if (!string.IsNullOrEmpty(config.SmtpUser) && !string.IsNullOrEmpty(config.SmtpPassword))
                client.Credentials = new NetworkCredential(config.SmtpUser, config.SmtpPassword);
            else
                client.UseDefaultCredentials = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeoutMs);
            await client.SendMailAsync(message, timeout.Token);
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return $"{host}:{config.SmtpPort} did not respond within {TimeoutMs / 1000}s";
        }
        catch (Exception ex)
        {
            // The message of a transport exception never carries the password; it is still scrubbed before it is stored or logged.
            var detail = ex.InnerException is null ? ex.Message : $"{ex.Message} ({ex.InnerException.Message})";
            var password = config.SmtpPassword;
            return string.IsNullOrEmpty(password) ? detail : detail.Replace(password, "***");
        }
    }
}

/// <summary>
/// No SMS provider ships with the system: every send reports "not configured", so the sign-in code (OTP) is skipped.
/// To add SMS, implement ISmsService for your provider and register it in Program.cs instead of this class.
/// </summary>
public sealed class NoSmsService : ISmsService
{
    public bool IsConfigured => false;

    public Task<NotificationResult> SendAsync(string mobile, string message, CancellationToken ct = default) =>
        Task.FromResult(NotificationResult.NotConfigured("لا توجد خدمة رسائل نصية مضبوطة."));
}
