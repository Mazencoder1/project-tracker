using System.Security.Claims;
using System.Text.Encodings.Web;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ProjectTracker.Web.Middleware;

public static class TrackerAuth
{
    public const string Scheme = "TrackerSession";
    public const string AdminOnlyPolicy = "AdminOnly";
    public const string ForgotPasswordRateLimit = "ForgotPassword";
    public const string AdminOrDeputyPolicy = "AdminOrDeputy";

    /// <summary>Set on HttpContext.Items when the session store could not be reached (503 instead of 401).</summary>
    public const string SessionStoreDown = "tracker.session-store-down";
    public const string HadSessionCookies = "tracker.had-session-cookies";

    public static SessionPayload SessionUser(this ClaimsPrincipal user) => new(
        user.FindFirstValue("uid") ?? "",
        user.FindFirstValue(ClaimTypes.Role) ?? "",
        user.FindFirstValue(ClaimTypes.Name) ?? "",
        user.FindFirstValue("via") ?? LoginMethod.Password,
        user.FindFirstValue("sid") ?? "",
        0);

    /// <summary>Home page of each role (same rule as the original proxy).</summary>
    public static string HomeFor(string role) => role == "pm" ? "/pm" : "/meeting";
}

/// <summary>
/// Cookie authentication for the signed session cookie. Every request is checked like the original
/// proxy did: valid signature and expiry, legacy cookies consistent with it, and the session row still
/// active in user_sessions (so logout, suspension and role changes take effect immediately).
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionTokens tokens,
    SessionService sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookies = Request.Cookies;
        var hadCookies = cookies.ContainsKey(SessionService.SessionCookie) || cookies.ContainsKey("user_id");
        Context.Items[TrackerAuth.HadSessionCookies] = hadCookies;

        var session = tokens.VerifySession(cookies[SessionService.SessionCookie]);
        var consistent = session is not null && cookies["user_id"] == session.Id && cookies["user_role"] == session.Role;
        if (session is null || !consistent) return AuthenticateResult.NoResult();

        try
        {
            if (!await sessions.IsActiveAsync(session, Context.RequestAborted)) return AuthenticateResult.NoResult();
        }
        catch (Exception ex)
        {
            // Not being able to verify a session must never grant access, and must not wipe a possibly valid cookie.
            Logger.LogError(ex, "Could not verify the session against the database");
            Context.Items[TrackerAuth.SessionStoreDown] = true;
            Context.Items[TrackerAuth.HadSessionCookies] = false;
            return AuthenticateResult.NoResult();
        }

        var identity = new ClaimsIdentity(
        [
            new Claim("uid", session.Id),
            new Claim(ClaimTypes.Role, session.Role),
            new Claim(ClaimTypes.Name, session.Name ?? ""),
            new Claim("sid", session.Sid),
            new Claim("via", session.Via),
        ], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var isApi = Request.Path.StartsWithSegments("/api");
        var storeDown = Context.Items.ContainsKey(TrackerAuth.SessionStoreDown);
        // An expired, tampered or revoked session is dropped so the browser stops sending rejected cookies.
        if (Context.Items[TrackerAuth.HadSessionCookies] is true) sessions.ClearCookies(Response);

        if (isApi)
        {
            await (storeDown
                ? ApiResults.Error("تعذّر التحقق من جلسة الدخول حاليًا. أعد المحاولة بعد قليل.", 503)
                : ApiResults.Unauthorized()).ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext { HttpContext = Context });
        }
        else
        {
            Response.Redirect("/login");
        }
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        await ApiResults.AdminOnly().ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext { HttpContext = Context });
    }
}
