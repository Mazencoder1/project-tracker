using System.Text.RegularExpressions;

namespace ProjectTracker.Web.Services.Email;

/// <param name="Success">True when the SMTP server accepted the message.</param>
/// <param name="Error">Why it failed (technical, secret-free); stored in EmailNotificationLog.ErrorMessage.</param>
/// <param name="Retryable">A transient failure (server unreachable) may be tried again; a disabled or unconfigured mailer may not.</param>
public sealed record EmailSendResult(bool Success, string? Error = null, bool Retryable = false)
{
    public static EmailSendResult Ok { get; } = new(true);
}

/// <summary>Sends one already-rendered HTML message. Knows nothing about projects or templates.</summary>
public interface IEmailService
{
    bool IsValidAddress(string? email);
    Task<EmailSendResult> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>
/// Validation and result mapping on top of the SMTP transport (IMailService: System.Net.Mail, primary + fallback host, settings
/// from Email:* / environment variables). It never throws: a failure is a result the caller records.
/// </summary>
public sealed partial class EmailService(IMailService transport, ILogger<EmailService> logger) : IEmailService
{
    // Deliberately conservative: one address, no display name, no quotes or angle brackets, a dotted domain.
    [GeneratedRegex(@"^[^@\s<>"",;:\\]+@[^@\s<>"",;:\\]+\.[^@\s<>"",;:\\]+$")]
    private static partial Regex AddressRegex();

    public bool IsValidAddress(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Trim().Length <= 254 && AddressRegex().IsMatch(email.Trim());

    public async Task<EmailSendResult> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!IsValidAddress(to)) return new EmailSendResult(false, "Recipient e-mail address is invalid.");
        try
        {
            var result = await transport.SendAsync(new MailRequest([to.Trim()], subject, htmlBody), ct);
            if (result.Ok) return EmailSendResult.Ok;
            var reason = result.Detail ?? result.Message ?? result.Reason ?? "unknown";
            // "not_configured" (mailer off, no host) will not fix itself; "send_failed" (server unreachable) may.
            return new EmailSendResult(false, reason, Retryable: result.Reason == "send_failed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The mail transport threw while sending an e-mail");
            return new EmailSendResult(false, $"Unexpected mail transport error: {ex.GetType().Name}", Retryable: true);
        }
    }
}
