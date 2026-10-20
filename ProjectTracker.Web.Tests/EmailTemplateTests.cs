using ProjectTracker.Web.Migrations.EmailNotifications;
using ProjectTracker.Web.Services.Email;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>Placeholders, validation, encoding and sanitization of the e-mail templates (no database, no SMTP).</summary>
public class EmailTemplateTests
{
    private static readonly EmailHtmlSanitizer Sanitizer = new();

    private static Dictionary<string, string?> Values(Action<Dictionary<string, string?>>? tweak = null)
    {
        var values = EmailTemplateCatalog.SampleValues("https://system.test/meeting/1").ToDictionary(p => p.Key, p => p.Value);
        tweak?.Invoke(values);
        return values;
    }

    [Fact]
    public void Catalogue_lists_every_variable_from_the_request()
    {
        var names = EmailTemplateCatalog.Variables.Select(v => v.Name).ToHashSet();
        foreach (var required in new[]
        {
            "UserName", "ProjectName", "ProjectDescription", "ProjectStatus", "ProjectProgress", "ProjectStartDate",
            "ProjectEndDate", "ProjectCompletedDate", "DaysRemaining", "DepartmentName", "ProjectUrl",
        })
            Assert.Contains(required, names);
    }

    [Fact]
    public void Seeds_exist_for_every_template_key_and_use_only_known_variables()
    {
        Assert.Equal(EmailTemplateKeys.All.OrderBy(k => k), EmailTemplateSeeds.All.Select(s => s.Key).OrderBy(k => k));
        foreach (var seed in EmailTemplateSeeds.All)
        {
            Assert.Empty(EmailTemplateEngine.Validate(seed.Subject, isSubject: true));
            Assert.Empty(EmailTemplateEngine.Validate(seed.BodyHtml, isSubject: false));
        }
    }

    [Fact]
    public void Assigned_subject_matches_the_requested_wording_and_comes_from_the_seed_not_from_code()
    {
        var seed = EmailTemplateSeeds.All.Single(s => s.Key == EmailTemplateKeys.ProjectAssigned);
        Assert.Equal("تم إسناد مشروع جديد لك - {{ProjectName}}", seed.Subject);
        Assert.Equal("تم إسناد مشروع جديد لك - مشروع س", EmailTemplateEngine.RenderSubject(seed.Subject, Values(v => v["ProjectName"] = "مشروع س")));
    }

    [Theory]
    [InlineData("{{Nope}}")]
    [InlineData("مرحبًا {{UserName}} {{ProjectNam}}")]
    public void Unknown_variables_are_rejected(string text) =>
        Assert.NotEmpty(EmailTemplateEngine.Validate(text, isSubject: false));

    [Theory]
    [InlineData("{{ ProjectName")]
    [InlineData("{{}}")]
    [InlineData("{{Project Name}}")]
    [InlineData("ProjectName}}")]
    public void Malformed_braces_are_rejected(string text) =>
        Assert.NotEmpty(EmailTemplateEngine.Validate(text, isSubject: false));

    [Fact]
    public void Whitespace_inside_braces_is_tolerated()
    {
        Assert.Empty(EmailTemplateEngine.Validate("{{ ProjectName }}", isSubject: false));
        Assert.Equal("س", EmailTemplateEngine.RenderSubject("{{ ProjectName }}", Values(v => v["ProjectName"] = "س")));
    }

    [Fact]
    public void Html_variable_is_not_allowed_in_the_subject() =>
        Assert.NotEmpty(EmailTemplateEngine.Validate("{{OverdueTasksList}}", isSubject: true));

    [Theory]
    [InlineData("""<a href="{{ProjectName}}">x</a>""")]
    [InlineData("""<a href='{{UserName}}'>x</a>""")]
    [InlineData("""<img src={{ProjectDescription}}>""")]
    public void Only_ProjectUrl_may_appear_inside_a_link_attribute(string html) =>
        Assert.NotEmpty(EmailTemplateEngine.Validate(html, isSubject: false));

    [Fact]
    public void ProjectUrl_inside_href_is_accepted() =>
        Assert.Empty(EmailTemplateEngine.Validate("""<a href="{{ProjectUrl}}">فتح</a>""", isSubject: false));

    [Fact]
    public void Body_values_are_html_encoded_so_a_project_name_cannot_inject_markup()
    {
        var html = EmailTemplateEngine.RenderBody("<p>{{ProjectName}}</p>", Values(v => v["ProjectName"] = "<script>alert(1)</script><img src=x onerror=alert(2)>"));
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Only_the_overdue_list_variable_is_inserted_as_raw_html()
    {
        var html = EmailTemplateEngine.RenderBody("{{OverdueTasksList}}", Values(v => v["OverdueTasksList"] = "<ul><li>x</li></ul>"));
        Assert.Equal("<ul><li>x</li></ul>", html);
    }

    [Fact]
    public void A_value_that_contains_a_placeholder_is_not_expanded_again()
    {
        var html = EmailTemplateEngine.RenderBody("{{ProjectName}}", Values(v => { v["ProjectName"] = "{{UserName}}"; v["UserName"] = "SECRET"; }));
        Assert.DoesNotContain("SECRET", html);
    }

    [Fact]
    public void Subject_values_cannot_inject_header_lines()
    {
        var subject = EmailTemplateEngine.RenderSubject("تنبيه - {{ProjectName}}", Values(v => v["ProjectName"] = "س\r\nBcc: attacker@evil.test"));
        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain('\n', subject);
    }

    // ── sanitizer ───────────────────────────────────────────────────────────
    [Fact]
    public void Sanitizer_removes_scripts_handlers_iframes_and_javascript_links()
    {
        var result = Sanitizer.Sanitize("""
            <p onclick="steal()">نص</p><script>alert(1)</script><iframe src="https://evil.test"></iframe>
            <a href="javascript:alert(1)">bad</a><form action="https://evil.test"><input name="p"></form>
            <img src="x" onerror="alert(2)"><style>body{display:none}</style><object data="x"></object>
            """);
        var html = result.Html.ToLowerInvariant();
        foreach (var bad in new[] { "<script", "onclick", "onerror", "<iframe", "javascript:", "<form", "<input", "<style", "<object" })
            Assert.DoesNotContain(bad, html);
        Assert.Contains("نص", result.Html);
        Assert.NotEmpty(result.Removed);
    }

    [Fact]
    public void Sanitizer_drops_data_and_vbscript_urls()
    {
        var html = Sanitizer.Sanitize("""<a href="data:text/html;base64,PHNjcmlwdD4=">a</a><a href="vbscript:x">b</a><a href="https://ok.test/p">c</a>""").Html;
        Assert.DoesNotContain("data:", html);
        Assert.DoesNotContain("vbscript:", html);
        Assert.Contains("https://ok.test/p", html);
    }

    [Fact]
    public void Sanitizer_keeps_the_layout_of_every_seed_and_the_ProjectUrl_placeholder()
    {
        foreach (var seed in EmailTemplateSeeds.All)
        {
            var result = Sanitizer.Sanitize(seed.BodyHtml);
            Assert.Empty(result.Removed);
            Assert.Contains("href=\"{{ProjectUrl}}\"", result.Html);
            Assert.Contains("dir=\"rtl\"", result.Html);
            Assert.Contains("<table", result.Html);
            Assert.Contains("border-radius", result.Html);
            Assert.Equal(EmailTemplateEngine.Placeholders(seed.BodyHtml).OrderBy(x => x), EmailTemplateEngine.Placeholders(result.Html).OrderBy(x => x));
        }
    }

    [Fact]
    public void A_javascript_value_smuggled_through_a_variable_into_an_href_is_neutralised_after_rendering()
    {
        // Validation rejects {{ProjectName}} inside href, but a database row edited by hand could still contain it:
        // the final sanitization pass is the second line of defence.
        var rendered = EmailTemplateEngine.RenderBody("""<a href="{{ProjectName}}">x</a>""", Values(v => v["ProjectName"] = "javascript:alert(1)"));
        Assert.DoesNotContain("javascript:", Sanitizer.Sanitize(rendered).Html);
    }
}
