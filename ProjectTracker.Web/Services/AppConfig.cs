namespace ProjectTracker.Web.Services;

/// <summary>
/// Typed access to every setting the Node.js system read from .env. Each value is looked up under the original
/// flat name first (SESSION_SECRET, DB_SERVER, ... — as an environment variable, so an existing .env can be reused
/// as-is and always wins over the file defaults) and then under its nested appsettings.json key (Session:Secret ...).
/// </summary>
public sealed class AppConfig(IConfiguration configuration, IHostEnvironment environment)
{
    /// <summary>Flat env name → nested key → alternative nested key (the layout used by the deployed appsettings.json).</summary>
    private string? Get(string nested, string legacy, params string[] aliases)
    {
        var value = configuration[legacy];
        if (string.IsNullOrWhiteSpace(value)) value = configuration[nested];
        foreach (var alias in aliases)
        {
            if (!string.IsNullOrWhiteSpace(value)) break;
            value = configuration[alias];
        }
        value = value?.Trim();
        // Template placeholders such as PUT_SMS_KEY_HERE mean "not configured yet".
        if (string.IsNullOrEmpty(value) || value.StartsWith("PUT_", StringComparison.Ordinal)) return null;
        return value;
    }

    private bool? Flag(string nested, string legacy, params string[] aliases) => Get(nested, legacy, aliases)?.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "y" => true,
        "false" or "0" or "no" or "n" => false,
        _ => null,
    };

    public bool IsProduction => environment.IsProduction();

    /// <summary>Secure flag of the session cookies: Security:CookieSecure, else on in Production.</summary>
    public bool CookieSecure => Flag("Security:CookieSecure", "COOKIE_SECURE") ?? IsProduction;

    /// <summary>
    /// Reverse proxies (comma-separated IPs) whose X-Forwarded-For/Proto are believed. Empty = the local machine only
    /// (IIS / ANCM on the same server), which is the framework default.
    /// </summary>
    public IReadOnlyList<System.Net.IPAddress> GetTrustedProxies() => (Get("Security:TrustedProxies", "TRUSTED_PROXIES") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(v => System.Net.IPAddress.TryParse(v, out var ip) ? ip : null)
        .OfType<System.Net.IPAddress>()
        .ToList();

    /// <summary>Session lifetime in seconds (Security:SessionHours, default 8 hours).</summary>
    public int SessionSeconds => int.TryParse(Get("Security:SessionHours", "SESSION_HOURS"), out var h) && h is > 0 and <= 72 ? h * 3600 : 8 * 3600;

    // ── Database ────────────────────────────────────────────────────────────
    public string? DbConnectionString => Get("Database:ConnectionString", "DB_CONNECTION_STRING", "ConnectionStrings:Default");
    public string DbServer => Get("Database:Server", "DB_SERVER") ?? "localhost";
    public string? DbInstance => Get("Database:Instance", "DB_INSTANCE");
    public string? DbPort => Get("Database:Port", "DB_PORT");
    public string? DbName => Get("Database:Name", "DB_NAME");
    public string? DbUser => Get("Database:User", "DB_USER");
    public string? DbPassword => Get("Database:Password", "DB_PASSWORD");
    public bool DbTrustedConnection => Flag("Database:TrustedConnection", "DB_TRUSTED_CONNECTION") ?? string.IsNullOrEmpty(DbUser);
    public bool DbEncrypt => Flag("Database:Encrypt", "DB_ENCRYPT") ?? false;
    public bool DbTrustServerCertificate => Flag("Database:TrustServerCertificate", "DB_TRUST_SERVER_CERTIFICATE") ?? true;

    // ── Session / OTP ───────────────────────────────────────────────────────
    public string? SessionSecret => Get("Session:Secret", "SESSION_SECRET", "Security:SessionSecret");
    public bool OtpEnabled => Flag("Otp:Enabled", "OTP_ENABLED") ?? true;
    /// <summary>Accounts (by email, comma-separated) whose password sign-in skips OTP — for test accounts only; empty by default.</summary>
    public IReadOnlySet<string> OtpSkipEmails => (Get("Otp:SkipEmails", "OTP_SKIP_EMAILS") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // ── Mail ────────────────────────────────────────────────────────────────
    public string SmtpHost => Get("Mail:Host", "SMTP_HOST", "Email:Host") ?? "";
    public string SmtpFallbackHost => Get("Mail:FallbackHost", "SMTP_FALLBACK_HOST", "Email:FallbackHost") ?? "";
    public int SmtpPort => int.TryParse(Get("Mail:Port", "SMTP_PORT", "Email:Port"), out var p) ? p : 25;
    public string MailDomain => Get("Mail:Domain", "MAIL_DOMAIN", "Email:Domain") ?? "example.com";
    public string MailFrom => Get("Mail:From", "MAIL_FROM", "Email:From") ?? "workflow";
    /// <summary>The full sender address (Email:FromEmail). When set it wins over MailFrom + MailDomain.</summary>
    public string? MailFromEmail => Get("Mail:FromEmail", "MAIL_FROM_EMAIL", "Email:FromEmail");
    public string MailDisplayName => Get("Mail:DisplayName", "MAIL_DISPLAY_NAME", "Email:FromName", "Email:DisplayName") ?? "منصة متابعة المشاريع";
    public string SmtpUser => Get("Mail:User", "SMTP_USER", "Email:Username", "Email:User") ?? "";
    public string SmtpPassword => Get("Mail:Password", "SMTP_PASSWORD", "Email:Password") ?? "";
    /// <summary>Email:EnableSsl (or the older Email:RequireTls): STARTTLS on the relay connection (default on).</summary>
    public bool SmtpRequireTls => Flag("Mail:RequireTls", "SMTP_REQUIRE_TLS", "Email:EnableSsl", "Email:RequireTls") ?? true;
    /// <summary>Master switch (Email:Enabled, default on): when off no e-mail leaves the system and every attempt is reported as not sent.</summary>
    public bool EmailEnabled => Flag("Mail:Enabled", "EMAIL_ENABLED", "Email:Enabled") ?? true;

    // ── Departments ─────────────────────────────────────────────────────────
    /// <summary>Owning-department choices for projects (Departments:List in appsettings.json, or DEPARTMENTS comma-separated).</summary>
    public IReadOnlyList<string> GetDepartments()
    {
        var fromEnv = Get("Departments:Csv", "DEPARTMENTS");
        var list = fromEnv is not null
            ? fromEnv.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : configuration.GetSection("Departments:List").Get<string[]>() ?? [];
        return list.Length > 0 ? list.Distinct(StringComparer.Ordinal).ToList() : DefaultDepartments;
    }

    private static readonly string[] DefaultDepartments = ["الإدارة العامة", "الشؤون المالية", "الموارد البشرية", "تقنية المعلومات", "الشؤون القانونية", "أخرى"];


    // ── Notifications ───────────────────────────────────────────────────────
    public string? AppBaseUrl => Get("Notifications:AppBaseUrl", "APP_BASE_URL", "Application:BaseUrl", "Notifications:BaseUrl");
    /// <summary>Route of one project inside the system, with {id} for the project id (Application:ProjectPath).
    /// /meeting/{id} was removed — the project now opens inside /meeting?project={id}.</summary>
    public string ProjectPathTemplate => Get("Application:ProjectPath", "APP_PROJECT_PATH") is { } p && p.StartsWith('/') && p.Contains("{id}") ? p : "/meeting?project={id}";
    /// <summary>How often the project-reminder job wakes up to check whether the weekly window is open (1–60 minutes, default 5).</summary>
    public int ReminderCheckMinutes => int.TryParse(Get("Notifications:ReminderCheckMinutes", "NOTIFY_REMINDER_CHECK_MINUTES"), out var m) && m is >= 1 and <= 60 ? m : 5;
    public string? NotifySecret => Get("Notifications:Secret", "NOTIFY_SECRET");
    public bool NotifyScheduler => Flag("Notifications:Scheduler", "NOTIFY_SCHEDULER", "Notifications:SchedulerEnabled") == true;
    public bool NotifySchedulerInDev => Flag("Notifications:SchedulerInDev", "NOTIFY_SCHEDULER_DEV") == true;
    public int NotifySchedulerMinutes
    {
        get
        {
            var raw = Get("Notifications:SchedulerMinutes", "NOTIFY_SCHEDULER_MINUTES");
            return double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 15 ? (int)Math.Floor(v) : 60;
        }
    }
    public string NotifyTimezone => Get("Notifications:Timezone", "NOTIFY_TIMEZONE") ?? "Asia/Riyadh";
    public int NotifyWeeklyHour
    {
        get
        {
            var raw = Get("Notifications:WeeklyHour", "NOTIFY_WEEKLY_HOUR");
            return int.TryParse(raw, out var v) && v is >= 0 and <= 23 ? v : 7;
        }
    }
}
