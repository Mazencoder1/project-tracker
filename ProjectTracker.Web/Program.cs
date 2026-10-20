using System.Text.Encodings.Web;
using System.Text.Json;
using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;
using ProjectTracker.Web.Services.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Every request body in this system is small JSON; a low ceiling stops multi-megabyte payloads (answered 413).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2 * 1024 * 1024);

// ── Configuration: appsettings.json / appsettings.{Environment}.json / environment variables ──
builder.Services.AddSingleton<AppConfig>();
builder.Services.AddSingleton<SessionTokens>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<PublicUrlResolver>();
builder.Services.AddSingleton<NotifyTemplates>();
builder.Services.AddSingleton<IMailService, SmtpMailService>();
builder.Services.AddSingleton<DepartmentService>();
// No SMS provider ships with the system (the sign-in code is then skipped); register your own ISmsService here.
builder.Services.AddSingleton<ISmsService, NoSmsService>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LoginThrottle>();

// ── Database (the existing SQL Server schema; EF Core never creates or alters it) ──
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var config = services.GetRequiredService<AppConfig>();
    options.UseSqlServer(ConnectionStrings.Build(config));
});

builder.Services.AddScoped<SqlDb>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ProjectAccess>();
builder.Services.AddScoped<AppSettingsService>();
builder.Services.AddScoped<PortfolioQueries>();
builder.Services.AddScoped<ApiExceptionFilter>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AccountNotifications>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddHostedService<NotificationScheduler>();

// ── E-mail notifications: project event → notifier → template service → e-mail service → SMTP ──
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEmailHtmlSanitizer, EmailHtmlSanitizer>();
builder.Services.AddSingleton<IEmailService, EmailService>();
builder.Services.AddSingleton<ProjectLinkBuilder>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<IEmailNotificationLog, EmailNotificationLogStore>();
builder.Services.AddScoped<WeeklyReminderSettingsService>();
builder.Services.AddScoped<EmailPipeline>();
builder.Services.AddScoped<IProjectEmailNotifier, ProjectEmailNotifier>();
builder.Services.AddSingleton<ProjectCompletionWatcher>();
builder.Services.AddHostedService<ProjectReminderScheduler>();

// ── Authentication (signed session cookie checked against user_sessions) and role policies ──
builder.Services.AddAuthentication(TrackerAuth.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(TrackerAuth.Scheme, null);
// Everything requires a session unless an endpoint says [AllowAnonymous] (login, logout, OTP, notification trigger).
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(TrackerAuth.Scheme).RequireAuthenticatedUser().Build())
    .AddPolicy(TrackerAuth.AdminOnlyPolicy, p => p.AddAuthenticationSchemes(TrackerAuth.Scheme).RequireRole("admin"))
    .AddPolicy(TrackerAuth.AdminOrDeputyPolicy, p => p.AddAuthenticationSchemes(TrackerAuth.Scheme).RequireRole("admin", "deputy"));

// ── MVC: JSON keeps the exact property names of the original API (no camelCase conversion, UTF-8 Arabic text) ──
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = null;
    o.JsonSerializerOptions.DictionaryKeyPolicy = null;
    o.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

builder.Services.AddTrackerRateLimiting();

builder.Services.AddTrackerForwardedHeaders();

var app = builder.Build();

StartupChecks.Verify(app.Services.GetRequiredService<AppConfig>());

app.UseForwardedHeaders();
app.UseCookiePolicy(new CookiePolicyOptions { Secure = CookieSecurePolicy.SameAsRequest });
// HSTS خارج التطوير فقط؛ الإطار يرسله على طلبات HTTPS وحدها ويستثني localhost.
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Unhandled errors never reach the user as raw exceptions.
app.UseExceptionHandler(errorApp => errorApp.Run(GlobalExceptionHandler.HandleAsync));

app.UseMiddleware<PageFileRedirectMiddleware>();
app.UseMiddleware<FlightPayloadMiddleware>();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Build output under /_next/static/ is content-hashed, so it can be cached forever (as Next.js served it).
        if (ctx.Context.Request.Path.StartsWithSegments("/_next/static"))
            ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    },
});

app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<OriginCheckMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
var publicUrl = app.Services.GetRequiredService<PublicUrlResolver>();
app.Use((context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true) publicUrl.Remember(context.Request);
    return next(context);
});
app.MapControllers();

if (app.Environment.IsDevelopment()) app.MapDevelopmentEndpoints();

await app.RunAsync();


// Note: يجعل نقطة الدخول مرئية لاختبارات التكامل (WebApplicationFactory<Program>) دون أي أثر على التشغيل.
public partial class Program
{
    protected Program() { }
}
