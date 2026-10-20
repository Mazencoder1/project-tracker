using System.Security.Claims;
using ProjectTracker.Web.Services;
using ProjectTracker.Web.Services.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>
/// the project link in e-mails follows the address the system is really opened with instead of a
/// fixed Application:BaseUrl (it was http://localhost:3000 / http://intranet-host:3000), and a missing link hides the button.
/// </summary>
public class PublicUrlResolverTests
{
    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static (PublicUrlResolver Resolver, HttpContextAccessor Http) Create(string? baseUrl = null)
    {
        var values = new Dictionary<string, string?> { ["Application:BaseUrl"] = baseUrl };
        var config = new AppConfig(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new Env());
        var http = new HttpContextAccessor();
        return (new PublicUrlResolver(config, http, NullLogger<PublicUrlResolver>.Instance), http);
    }

    private static DefaultHttpContext Request(string host, bool signedIn, string scheme = "https")
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        if (signedIn) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "u")], "test"));
        return context;
    }

    [Fact]
    public void Configured_address_wins_when_filled_in()
    {
        var (resolver, http) = Create("https://pm.example.com/");
        http.HttpContext = Request("other.example", signedIn: true);
        Assert.Equal("https://pm.example.com", resolver.BaseUrl());
    }

    [Fact]
    public void Without_configuration_the_signed_in_request_address_is_used()
    {
        var (resolver, http) = Create();
        http.HttpContext = Request("pm.example.com:8443", signedIn: true);
        Assert.Equal("https://pm.example.com:8443", resolver.BaseUrl());
    }

    [Fact]
    public void Anonymous_request_host_is_never_trusted()
    {
        var (resolver, http) = Create();
        resolver.Remember(Request("pm.example.com", signedIn: true).Request);
        http.HttpContext = Request("evil.example", signedIn: false);
        Assert.Equal("https://pm.example.com", resolver.BaseUrl());
    }

    [Fact]
    public void Scheduled_job_uses_the_last_real_address_and_ignores_loopback()
    {
        var (resolver, _) = Create();
        resolver.Remember(Request("pm.example.com", signedIn: true).Request);
        resolver.Remember(Request("localhost:3000", signedIn: true, "http").Request);
        Assert.Equal("https://pm.example.com", resolver.BaseUrl());
    }

    [Fact]
    public void Loopback_is_used_only_when_nothing_else_is_known()
    {
        var (resolver, _) = Create();
        Assert.Null(resolver.BaseUrl());
        resolver.Remember(Request("localhost:3000", signedIn: true, "http").Request);
        Assert.Equal("http://localhost:3000", resolver.BaseUrl());
    }

    [Fact]
    public void Project_link_uses_the_resolved_address()
    {
        var values = new Dictionary<string, string?>();
        var config = new AppConfig(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new Env());
        var http = new HttpContextAccessor { HttpContext = Request("pm.example.com", signedIn: true) };
        var links = new ProjectLinkBuilder(config, new PublicUrlResolver(config, http, NullLogger<PublicUrlResolver>.Instance));
        Assert.Equal("https://pm.example.com/meeting?project=abc", links.Build("ABC"));
    }

    [Theory]
    [InlineData(EmailTemplateKeys.ProjectAssigned)]
    [InlineData(EmailTemplateKeys.ProjectWeeklyReminder)]
    [InlineData(EmailTemplateKeys.ProjectCompleted)]
    public void Missing_link_hides_the_button_and_a_present_link_keeps_it(string key)
    {
        var frame = EmailTemplateShells.Build(key, "نص");
        var without = EmailTemplateCatalog.SampleValues(null).ToDictionary(p => p.Key, p => p.Value);
        without[EmailTemplateCatalog.ProjectUrlVariable] = "";
        var hidden = EmailTemplateEngine.RenderBody(frame, without);
        Assert.DoesNotContain("فتح المشروع", hidden);
        Assert.DoesNotContain("<!--", hidden);

        var shown = EmailTemplateEngine.RenderBody(frame, EmailTemplateCatalog.SampleValues("https://pm.example.com/meeting?project=1"));
        Assert.Contains("href=\"https://pm.example.com/meeting?project=1\"", shown);
        Assert.Contains("منصة متابعة المشاريع", shown);
        Assert.DoesNotContain("<!--", shown);
    }
}
