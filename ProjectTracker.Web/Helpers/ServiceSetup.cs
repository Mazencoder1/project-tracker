using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;

namespace ProjectTracker.Web.Helpers;

/// <summary>Service and endpoint registration that used to sit in Program.cs.</summary>
public static class ServiceSetup
{
    private static readonly JsonSerializerOptions RateLimitJsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Password-recovery requests are limited per client address.</summary>
    public static IServiceCollection AddTrackerRateLimiting(this IServiceCollection services)
    {
        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(TrackerAuth.ForgotPasswordRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
            o.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(new { error = "محاولات كثيرة لاستعادة كلمة المرور — حاول مرة أخرى بعد 10 دقائق." },
                    RateLimitJsonOptions), ct);
            };
        });
    }

    public static IServiceCollection AddTrackerForwardedHeaders(this IServiceCollection services)
    {
        return services.AddOptions<ForwardedHeadersOptions>().Configure<AppConfig>((o, config) =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.ForwardLimit = 1;
            if (config.GetTrustedProxies() is { Count: > 0 } proxies)
            {
                o.KnownProxies.Clear();
                o.KnownNetworks.Clear();
                foreach (var proxy in proxies) o.KnownProxies.Add(proxy);
            }
        }).Services;
    }

    /// <summary>Development-only manual test of the SMTP relay (admin session required even there).</summary>
    public static WebApplication MapDevelopmentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/dev/mail-test", async (MailTestRequest body, IMailService mail) =>
        {
            if (string.IsNullOrWhiteSpace(body.To) || string.IsNullOrWhiteSpace(body.Subject) || string.IsNullOrWhiteSpace(body.Body))
                return Results.BadRequest(new { error = "الحقول To وSubject وBody مطلوبة." });
            return Results.Json(await mail.SendAsync(new MailRequest([body.To], body.Subject, body.Body)));
        }).RequireAuthorization(TrackerAuth.AdminOnlyPolicy);
        return app;
    }
}

/// <summary>Fail fast with a clear message instead of answering every request with a generic 500.</summary>
public static class StartupChecks
{
    public static void Verify(AppConfig config)
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(config.DbConnectionString) && string.IsNullOrEmpty(config.DbName)) missing.Add("DB_NAME (اسم قاعدة البيانات) مع DB_SERVER، وDB_USER/DB_PASSWORD أو اتركهما فارغين لمصادقة ويندوز");
        if (config.SessionSecret is not { Length: >= 32 }) missing.Add("SESSION_SECRET (32 حرفًا على الأقل)");
        if (missing.Count > 0)
            throw new InvalidOperationException("إعدادات ناقصة، لا يمكن تشغيل النظام: " + string.Join(" · ", missing)
                + ". اضبطها كمتغيرات بيئة أو في appsettings.Development.json (راجع ProjectTracker.Web/README.md).");
    }
}
