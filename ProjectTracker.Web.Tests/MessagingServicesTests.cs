using ProjectTracker.Web.Data;
using ProjectTracker.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>Unit coverage for the SMTP relay (mocked, no real mail sent) and the default no-SMS provider.</summary>
public class MessagingServicesTests
{
    private static AppConfig Config(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new Env());

    private static readonly IServiceScopeFactory AuditScopes = new ServiceCollection()
        .AddLogging()
        .AddDbContext<AppDbContext>(o => o.UseSqlServer("Server=127.0.0.1,1;Database=none;Connect Timeout=1;Encrypt=False"))
        .AddScoped<SqlDb>()
        .AddScoped<AuditService>()
        .BuildServiceProvider()
        .GetRequiredService<IServiceScopeFactory>();

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // ── SMS ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_sms_provider_reports_not_configured()
    {
        var sms = new NoSmsService();
        Assert.False(sms.IsConfigured);
        var result = await sms.SendAsync("0500000001", "test");
        Assert.False(result.Ok);
        Assert.Equal("not_configured", result.Reason);
    }

    // ── Email ──────────────────────────────────────────────────────────────

    private static SmtpMailService Mail(FakeSmtpServer primary, FakeSmtpServer? fallback = null, bool enabled = true) => new(
        Config(new Dictionary<string, string?>
        {
            ["Email:Enabled"] = enabled.ToString(),
            ["Mail:Host"] = "127.0.0.1",
            ["Mail:Port"] = primary.Port.ToString(),
            ["Mail:FallbackHost"] = fallback is null ? "" : "127.0.0.1",
            ["Mail:FromEmail"] = "workflow@example.com",
            ["Mail:RequireTls"] = "false",
        }), NullLogger<SmtpMailService>.Instance, AuditScopes);

    [Fact]
    public async Task Mail_disabled_never_touches_the_network()
    {
        using var primary = new FakeSmtpServer();
        var result = await Mail(primary, enabled: false).SendAsync(new MailRequest(["a@test.example"], "s", "b"));

        Assert.False(result.Ok);
        Assert.Equal("not_configured", result.Reason);
        Assert.Empty(primary.Mails);
    }

    [Fact]
    public async Task Mail_delivers_to_and_cc_subject_and_html_body_via_primary()
    {
        using var primary = new FakeSmtpServer();

        var result = await Mail(primary).SendAsync(new MailRequest(
            MailTo: ["to@test.example"], MailSubject: "عنوان الاختبار", MailBody: "<b>محتوى</b>", MailCc: ["cc@test.example"]));

        Assert.True(result.Ok);
        var mail = Assert.Single(primary.Mails);
        Assert.Contains("to@test.example", mail.To);
        Assert.Contains("cc@test.example", mail.To);
        Assert.Equal("عنوان الاختبار", mail.Subject);
        Assert.Equal("<b>محتوى</b>", mail.HtmlBody);
    }

    [Fact]
    public async Task Mail_falls_back_to_the_secondary_server_when_the_primary_is_unreachable()
    {
        using var fallback = new FakeSmtpServer();
        // Note: the port is shared between both hosts (config.SmtpPort), so the primary host is a loopback
        // alias nothing listens on — a fast "connection refused" rather than a real reject/timeout.
        var service = new SmtpMailService(Config(new Dictionary<string, string?>
        {
            ["Email:Enabled"] = "true",
            ["Mail:Host"] = "127.0.0.2",
            ["Mail:Port"] = fallback.Port.ToString(),
            ["Mail:FallbackHost"] = "127.0.0.1",
            ["Mail:FromEmail"] = "workflow@example.com",
            ["Mail:RequireTls"] = "false",
        }), NullLogger<SmtpMailService>.Instance, AuditScopes);

        var result = await service.SendAsync(new MailRequest(["a@test.example"], "s", "b"));

        Assert.True(result.Ok);
        Assert.Single(fallback.Mails);
    }

    [Fact]
    public async Task Mail_reports_failure_when_both_servers_reject()
    {
        using var primary = new FakeSmtpServer { Reject = true };
        var result = await Mail(primary, primary).SendAsync(new MailRequest(["a@test.example"], "s", "b"));

        Assert.False(result.Ok);
        Assert.Equal("send_failed", result.Reason);
    }
}
