using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Services;
using Xunit;

namespace ProjectTracker.Web.Tests;

public class ConfigAndHelperTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static AppConfig Config(Dictionary<string, string?> values, string environment = "Development") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new FakeEnvironment(environment));

    [Fact]
    public void Settings_are_read_from_flat_names_nested_names_and_aliases_with_sensible_defaults()
    {
        var defaults = Config([]);
        Assert.False(defaults.IsProduction);
        Assert.False(defaults.CookieSecure);
        Assert.Equal(8 * 3600, defaults.SessionSeconds);
        Assert.Equal("localhost", defaults.DbServer);
        Assert.True(defaults.DbTrustedConnection);
        Assert.False(defaults.DbEncrypt);
        Assert.True(defaults.DbTrustServerCertificate);
        Assert.True(defaults.OtpEnabled);
        Assert.Empty(defaults.OtpSkipEmails);
        Assert.Equal(25, defaults.SmtpPort);
        Assert.Equal("example.com", defaults.MailDomain);
        Assert.True(defaults.SmtpRequireTls);
        Assert.True(defaults.EmailEnabled);
        Assert.NotEmpty(defaults.GetDepartments());
        Assert.Empty(defaults.GetTrustedProxies());
        Assert.Equal("Asia/Riyadh", defaults.NotifyTimezone);
        Assert.False(defaults.NotifyScheduler);
        Assert.False(defaults.NotifySchedulerInDev);
        Assert.True(Config([], "Production").CookieSecure);

        var custom = Config(new()
        {
            ["DB_SERVER"] = "db-host", ["Database:Instance"] = "SQLX", ["DB_PORT"] = "1444", ["Database:Name"] = "Tracker", ["DB_USER"] = "sa", ["DB_PASSWORD"] = "secret",
            ["Security:SessionHours"] = "12", ["Security:TrustedProxies"] = "10.0.0.1, not-an-ip, 10.0.0.2", ["Security:CookieSecure"] = "yes",
            ["Otp:SkipEmails"] = "A@x.com, b@x.com", ["Email:Host"] = "mail", ["Email:Port"] = "2525", ["Email:EnableSsl"] = "no", ["Mail:Enabled"] = "0",
            ["Departments:Csv"] = "أ,ب,أ", ["Application:ProjectPath"] = "/p/{id}", ["Notifications:ReminderCheckMinutes"] = "15",
            ["Notifications:Secret"] = "PUT_SECRET_HERE", ["Notifications:SchedulerEnabled"] = "true", ["Notifications:SchedulerInDev"] = "true",
        });
        Assert.Equal(12 * 3600, custom.SessionSeconds);
        Assert.Equal(2, custom.GetTrustedProxies().Count);
        Assert.True(custom.CookieSecure);
        Assert.False(custom.DbTrustedConnection);
        Assert.Contains("a@x.com", custom.OtpSkipEmails);
        Assert.Equal(2525, custom.SmtpPort);
        Assert.False(custom.SmtpRequireTls);
        Assert.False(custom.EmailEnabled);
        Assert.Equal(["أ", "ب"], custom.GetDepartments());
        Assert.Equal("/p/{id}", custom.ProjectPathTemplate);
        Assert.Equal(15, custom.ReminderCheckMinutes);
        Assert.Null(custom.NotifySecret);
        Assert.True(custom.NotifyScheduler);
        Assert.True(custom.NotifySchedulerInDev);
        Assert.InRange(Config(new() { ["Security:SessionHours"] = "500" }).SessionSeconds, 1, 8 * 3600);
        Assert.Equal(Config([]).ProjectPathTemplate, Config(new() { ["Application:ProjectPath"] = "no-slash" }).ProjectPathTemplate);
        _ = custom.MailFromEmail; _ = custom.MailFrom; _ = custom.MailDisplayName; _ = custom.SmtpUser; _ = custom.SmtpPassword; _ = custom.SmtpFallbackHost;
        _ = custom.AppBaseUrl; _ = custom.NotifySchedulerMinutes; _ = custom.NotifyWeeklyHour; _ = custom.SessionSecret; _ = custom.DbConnectionString;
    }

    [Fact]
    public void The_connection_string_is_built_from_the_parts_or_taken_as_given()
    {
        Assert.Equal("Server=ready", ConnectionStrings.Build(Config(new() { ["DB_CONNECTION_STRING"] = "Server=ready" })));

        var windows = ConnectionStrings.Build(Config(new() { ["DB_NAME"] = "T", ["DB_SERVER"] = "host", ["DB_INSTANCE"] = "INST", ["DB_PORT"] = "1500" }));
        Assert.Contains("Integrated Security=True", windows);
        Assert.Contains("host\\INST,1500", windows);

        var sql = ConnectionStrings.Build(Config(new() { ["DB_NAME"] = "T", ["DB_USER"] = "u", ["DB_PASSWORD"] = "p" }));
        Assert.Contains("User ID=u", sql);

        Assert.Throws<InvalidOperationException>(() => ConnectionStrings.Build(Config([])));
        Assert.Throws<InvalidOperationException>(() => ConnectionStrings.Build(Config(new() { ["DB_NAME"] = "T", ["DB_USER"] = "u" })));
    }

    [Fact]
    public void Startup_checks_name_every_missing_setting()
    {
        var error = Assert.Throws<InvalidOperationException>(() => StartupChecks.Verify(Config([])));
        Assert.Contains("DB_NAME", error.Message);
        Assert.Contains("SESSION_SECRET", error.Message);
        StartupChecks.Verify(Config(new() { ["DB_NAME"] = "T", ["SESSION_SECRET"] = new string('x', 40) }));
    }

    [Fact]
    public void Number_and_text_helpers_follow_the_javascript_behaviour_the_api_was_ported_from()
    {
        Assert.True(Js.IsUuid(Guid.NewGuid().ToString()));
        Assert.False(Js.IsUuid("nope"));
        Assert.False(Js.IsUuid(null));
        Assert.Equal(3, Js.Round(2.5));
        Assert.Equal(-2, Js.Round(-2.5));
        Assert.Equal(12, Js.ParseInt("12px"));
        Assert.Equal(-5, Js.ParseInt(" -5"));
        Assert.Null(Js.ParseInt("abc"));
        Assert.Null(Js.ParseInt(null));
        Assert.Equal("5", Js.NumberToString(5.0));
        Assert.Equal("5.5", Js.NumberToString(5.5));
        Assert.Equal("0123456789", Js.NormalizeDigits("٠١٢٣٤٥٦٧٨٩"));
        Assert.Equal("0123", Js.NormalizeDigits("۰۱۲۳"));
        Assert.True(Js.IsIsoDate("2026-02-28"));
        Assert.False(Js.IsIsoDate("2026-02-30"));
        Assert.False(Js.IsIsoDate("28/02/2026"));
        Assert.False(Js.IsIsoDate(null));
        Assert.Equal(10, Js.Today().Length);

        using var doc = JsonDocument.Parse("""{"s":" نص ","n":5,"d":5.5,"t":true,"z":null,"o":{"a":1}}""");
        var root = doc.RootElement;
        Assert.Equal("نص", Js.Text(root.GetProperty("s"))?.Trim());
        Assert.Equal("5", Js.Text(root.GetProperty("n")));
        Assert.Equal("5.5", Js.Text(root.GetProperty("d")));
        Assert.Null(Js.Text(root.GetProperty("t")));
        Assert.Null(Js.Text(root.GetProperty("z")));
        Assert.Null(Js.Text(null));
        Assert.Null(Js.StringOrNull(root.GetProperty("n")));
        Assert.Equal(" نص ", Js.StringOrNull(root.GetProperty("s")));
        Assert.Null(Js.StringOrNull(null));
        Assert.Throws<JsonException>(() => Js.SafeString(root.GetProperty("o")));
    }

    [Fact]
    public void Error_results_never_leak_exception_text_and_map_known_failures()
    {
        var logger = NullLogger.Instance;

        Assert.Equal(400, ApiResults.ServerError(new JsonException("x"), logger).StatusCode);
        Assert.Equal(413, ApiResults.ServerError(new BadHttpRequestException("big", 413), logger).StatusCode);
        Assert.Equal(400, ApiResults.ServerError(new FormatException("x"), logger).StatusCode);
        Assert.Equal(400, ApiResults.ServerError(new OverflowException("x"), logger).StatusCode);
        var generic = ApiResults.ServerError(new InvalidOperationException("secret SQL text"), logger, "ctx");
        Assert.Equal(500, generic.StatusCode);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(generic.Value));

        Assert.Equal(400, ApiResults.BadBody().StatusCode);
        Assert.Equal(401, ApiResults.Unauthorized().StatusCode);
        Assert.Equal(403, ApiResults.AdminOnly().StatusCode);
        Assert.Equal(400, ApiResults.ValidationFailed(new() { ["a"] = "b" }).StatusCode);
        Assert.Equal(404, ApiResults.Error("x", 404).StatusCode);
    }
}
