using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>The whole "forgot my password" journey with the e-mails read from a fake SMTP relay.</summary>
public partial class PasswordRecoveryTests
{
    [GeneratedRegex(@"reset_token=([^""&\s<]+)")] private static partial Regex TokenRegex();

    [GeneratedRegex(@"letter-spacing:2px[^>]*>([^<]+)<")] private static partial Regex PasswordRegex();

    [SqlFact]
    public async Task A_link_then_a_new_password_by_mail_lets_the_user_sign_in_again()
    {
        await using var sb = await Sandbox.CreateAsync();
        var (_, email) = await sb.NewUserAsync("استعادة");
        var anonymous = sb.Anonymous();

        await Http.Expect(HttpStatusCode.OK, await Http.Post(anonymous, "/api/auth/forgot-password", new { email }));
        var linkMail = Assert.Single(sb.Smtp.To(email));
        var token = TokenRegex().Match(linkMail.HtmlBody).Groups[1].Value;
        Assert.NotEmpty(token);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Http.Post(anonymous, "/api/auth/forgot-password", new { email })).StatusCode);

        sb.Smtp.Clear();
        await Http.Expect(HttpStatusCode.OK, await Http.Post(anonymous, "/api/auth/forgot-password/confirm", new { token = System.Net.WebUtility.HtmlDecode(token) }));
        var passwordMail = Assert.Single(sb.Smtp.To(email));
        var password = PasswordRegex().Match(passwordMail.HtmlBody).Groups[1].Value;
        Assert.NotEmpty(password);
        var signedIn = await sb.LoginAsync(email, System.Net.WebUtility.HtmlDecode(password));

        // The link is single-use: the password changed, so the same token no longer matches.
        Assert.Equal(HttpStatusCode.Gone, (await Http.Post(anonymous, "/api/auth/forgot-password/confirm", new { token = System.Net.WebUtility.HtmlDecode(token) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/api/projects")).StatusCode);
    }

    [SqlFact]
    public async Task Signed_in_reset_mails_a_password_and_a_suspended_account_is_refused()
    {
        await using var sb = await Sandbox.CreateAsync();
        var (id, email) = await sb.NewUserAsync("ذاتي");
        var pm = await sb.LoginAsync(email, Sandbox.UserPassword);

        await Http.Expect(HttpStatusCode.OK, await Http.Post(pm, "/api/auth/reset-password"));
        Assert.Single(sb.Smtp.To(email));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Http.Post(pm, "/api/auth/reset-password")).StatusCode);

        await Sandbox.ExecuteAsync("UPDATE profiles SET status = 'disabled' WHERE id = @i", ("@i", id));
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.Post(sb.Anonymous(), "/api/auth/forgot-password", new { email })).StatusCode);

        sb.Smtp.Reject = true;
        var (_, other) = await sb.NewUserAsync("بريد معطّل");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Http.Post(sb.Anonymous(), "/api/auth/forgot-password", new { email = other })).StatusCode);
    }

    [SqlFact]
    public async Task An_account_without_a_password_cannot_be_recovered_by_mail()
    {
        await using var sb = await Sandbox.CreateAsync();
        var (_, email) = await sb.NewUserAsync("بلا كلمة مرور");
        await Sandbox.ExecuteAsync("UPDATE profiles SET password_hash = NULL WHERE email = @e", ("@e", email));
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(sb.Anonymous(), "/api/auth/forgot-password", new { email })).StatusCode);
    }
}
