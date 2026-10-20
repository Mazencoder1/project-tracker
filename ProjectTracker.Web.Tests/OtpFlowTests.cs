using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ProjectTracker.Web.Services;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>An SMS gateway that keeps the messages so a test can read the one-time code.</summary>
public sealed partial class CapturingSms : ISmsService
{
    [GeneratedRegex(@"\d{6}")] private static partial Regex CodeRegex();

    public bool IsConfigured => true;

    public bool Fail { get; set; }

    public List<(string Mobile, string Message)> Sent { get; } = [];

    public Task<NotificationResult> SendAsync(string mobile, string message, CancellationToken ct = default)
    {
        if (Fail) return Task.FromResult(NotificationResult.SendFailed("gateway down"));
        Sent.Add((mobile, message));
        return Task.FromResult(NotificationResult.Success);
    }

    public string LastCode() => CodeRegex().Match(Sent[^1].Message).Value;
}

public class OtpFlowTests
{
    private static async Task<(Sandbox Sb, CapturingSms Sms, string Email)> StartAsync()
    {
        var sms = new CapturingSms();
        var sb = await Sandbox.CreateAsync(services => services.AddSingleton<ISmsService>(sms));
        var (id, email) = await sb.NewUserAsync("رمز التحقق");
        await Sandbox.ExecuteAsync("UPDATE profiles SET mobile_no = N'0501234567' WHERE id = @i", ("@i", id));
        return (sb, sms, email);
    }

    [SqlFact]
    public async Task The_sign_in_code_is_required_checked_and_then_opens_the_session()
    {
        var (sb, sms, email) = await StartAsync();
        await using var guard = sb;
        var client = sb.Anonymous();

        var login = await Http.Post(client, "/api/auth/login", new { email, password = Sandbox.UserPassword });
        await Http.Expect(HttpStatusCode.OK, login);
        Assert.True((await Http.Json(login)).GetProperty("otpRequired").GetBoolean());
        Assert.Single(sms.Sent);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/projects")).StatusCode);

        var pending = await Http.GetJson(client, "/api/auth/otp/resend");
        Assert.True(pending.GetProperty("pending").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(client, "/api/auth/otp/verify", new { code = "12" })).StatusCode);
        var wrong = await Http.Post(client, "/api/auth/otp/verify", new { code = sms.LastCode() == "000000" ? "111111" : "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.True((await Http.Json(wrong)).TryGetProperty("attemptsLeft", out _));

        var tooSoon = await Http.Post(client, "/api/auth/otp/resend");
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);

        await Http.Expect(HttpStatusCode.OK, await Http.Post(client, "/api/auth/otp/verify", new { code = sms.LastCode() }));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/projects")).StatusCode);
    }

    [SqlFact]
    public async Task Too_many_wrong_codes_and_an_unreachable_gateway_both_fail_closed()
    {
        var (sb, sms, email) = await StartAsync();
        await using var guard = sb;

        var client = sb.Anonymous();
        await Http.Post(client, "/api/auth/login", new { email, password = Sandbox.UserPassword });
        var wrongCode = sms.LastCode() == "000000" ? "111111" : "000000";
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 8 && last is not HttpStatusCode.Gone; i++)
            last = (await Http.Post(client, "/api/auth/otp/verify", new { code = wrongCode })).StatusCode;
        Assert.Equal(HttpStatusCode.Gone, last);
        Assert.Equal(HttpStatusCode.Gone, (await Http.Post(client, "/api/auth/otp/verify", new { code = wrongCode })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Http.Post(client, "/api/auth/otp/resend")).StatusCode);

        sms.Fail = true;
        var failed = await Http.Post(sb.Anonymous(), "/api/auth/login", new { email, password = Sandbox.UserPassword });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
    }

    [SqlFact]
    public async Task A_code_can_be_resent_once_the_cooldown_has_passed()
    {
        var (sb, sms, email) = await StartAsync();
        await using var guard = sb;
        var client = sb.Anonymous();
        await Http.Post(client, "/api/auth/login", new { email, password = Sandbox.UserPassword });

        await Sandbox.ExecuteAsync("UPDATE login_otp SET last_sent_at = DATEADD(minute, -10, SYSUTCDATETIME()) WHERE user_id IN (SELECT id FROM profiles WHERE email = @e)", ("@e", email));
        await Http.Expect(HttpStatusCode.OK, await Http.Post(client, "/api/auth/otp/resend"));
        Assert.Equal(2, sms.Sent.Count);
        await Http.Expect(HttpStatusCode.OK, await Http.Post(client, "/api/auth/otp/verify", new { code = sms.LastCode() }));
    }
}

