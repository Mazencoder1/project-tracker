using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using Microsoft.Data.SqlClient;

namespace ProjectTracker.Web.Services;

/// <summary>How the session was proven (e-mail + password is the only method).</summary>
public static class LoginMethod
{
    public const string Password = "password";
}

/// <summary>Contents of the signed session cookie (same fields as the original system).</summary>
public sealed record SessionPayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("via")] string Via,
    [property: JsonPropertyName("sid")] string Sid,
    [property: JsonPropertyName("exp")] long Exp);

/// <summary>HMAC-SHA256 signed tokens: <c>base64url(json).base64url(signature)</c>. Fail-closed without a secret.</summary>
public sealed class SessionTokens(AppConfig config)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private byte[]? Secret => config.SessionSecret is { Length: >= 32 } s ? Encoding.UTF8.GetBytes(s) : null;

    public string Sign(object payload)
    {
        var secret = Secret ?? throw new InvalidOperationException("SESSION_SECRET ط؛ظٹط± ظ…ط¶ط¨ظˆط· ط£ظˆ ط£ظ‚طµط± ظ…ظ† 32 ط­ط±ظپظ‹ط§");
        var body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signature = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(body));
        return $"{body}.{Base64Url(signature)}";
    }

    /// <summary>Verifies the signature only and returns the payload (no field or expiry checks).</summary>
    public T? Verify<T>(string? token) where T : class
    {
        var secret = Secret;
        if (secret is null || string.IsNullOrEmpty(token)) return null;
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot != token.LastIndexOf('.')) return null;
        var body = token[..dot];
        var signature = FromBase64Url(token[(dot + 1)..]);
        var raw = FromBase64Url(body);
        if (signature is null || raw is null) return null;

        var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(body));
        if (!CryptographicOperations.FixedTimeEquals(expected, signature)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string SignSession(string id, string role, string name, string sid, string via, int maxAgeSec) =>
        Sign(new SessionPayload(id, role, name ?? "", LoginMethod.Password, sid,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() + maxAgeSec));

    /// <summary>Verifies signature and expiry; null for any invalid token.</summary>
    public SessionPayload? VerifySession(string? token)
    {
        var p = Verify<SessionPayload>(token);
        if (p is null || p.Id is null || p.Role is null || string.IsNullOrEmpty(p.Sid)) return null;
        if (p.Exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
        return p with { Via = LoginMethod.Password };
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string value)
    {
        try
        {
            var s = value.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Server-side session state (table user_sessions) and the session cookies.</summary>
public sealed class SessionService(SqlDb db, SessionTokens tokens, AppConfig config)
{
    public const string SessionCookie = "tracker_session";

    private static readonly string[] LegacyCookies = ["user_id", "user_role", "user_name"];

    private CookieOptions Options(int maxAgeSec) => new() // NOSONAR S2092 — Secure comes from Security:CookieSecure (on in Production); internal installs may run over plain HTTP, where a Secure cookie would be dropped
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = config.CookieSecure,
        Path = "/",
        MaxAge = TimeSpan.FromSeconds(maxAgeSec),
    };

    /// <summary>Creates the session row, then issues the signed cookie plus the three legacy cookies.</summary>
    public async Task StartSessionAsync(HttpResponse response, string userId, string role, string name, string via = LoginMethod.Password, CancellationToken ct = default)
    {
        var rows = await db.QueryMultiAsync(
            """
            DELETE FROM user_sessions WHERE expires_at < DATEADD(day, -1, SYSUTCDATETIME());
            INSERT INTO user_sessions (user_id, expires_at)
            OUTPUT INSERTED.id
            VALUES (@user_id, DATEADD(second, @ttl, SYSUTCDATETIME()));
            """,
            [SqlDb.Guid("@user_id", userId), SqlDb.Int("@ttl", config.SessionSeconds)], ct);
        var sid = rows[^1][0].Str("id")!;

        var token = tokens.SignSession(userId, role, name, sid, via, config.SessionSeconds);
        var options = Options(config.SessionSeconds);
        response.Cookies.Append(SessionCookie, token, options);
        response.Cookies.Append("user_id", userId, options);
        response.Cookies.Append("user_role", role, options);
        response.Cookies.Append("user_name", name ?? "", options);
    }

    /// <summary>Not revoked, not expired, account active and still holding the role recorded in the session.</summary>
    public async Task<bool> IsActiveAsync(SessionPayload session, CancellationToken ct = default)
    {
        if (!Js.IsUuid(session.Sid) || !Js.IsUuid(session.Id)) return false;
        var row = await db.FirstAsync(
            """
            SELECT TOP 1 1 AS ok
            FROM user_sessions s
            JOIN profiles p ON p.id = s.user_id
            WHERE s.id = @sid AND s.user_id = @user_id
              AND s.revoked_at IS NULL AND s.expires_at > SYSUTCDATETIME()
              AND ISNULL(p.status, 'active') = 'active' AND p.role = @role
            """,
            [SqlDb.Guid("@sid", session.Sid), SqlDb.Guid("@user_id", session.Id), SqlDb.Text("@role", session.Role, 20)], ct);
        return row is not null;
    }

    /// <summary>Revokes every session of an account (suspend / delete / role change / password reset), optionally sparing one.</summary>
    public async Task RevokeUserSessionsAsync(string userId, string? exceptSid = null, CancellationToken ct = default)
    {
        if (!Js.IsUuid(userId)) return;
        var parameters = new List<SqlParameter> { SqlDb.Guid("@user_id", userId) };
        var filter = "";
        if (exceptSid is not null && Js.IsUuid(exceptSid))
        {
            parameters.Add(SqlDb.Guid("@except_sid", exceptSid));
            filter = " AND id <> @except_sid";
        }
        await db.ExecuteAsync($"UPDATE user_sessions SET revoked_at = SYSUTCDATETIME() WHERE user_id = @user_id AND revoked_at IS NULL{filter}", parameters, ct);
    }

    /// <summary>Logout: revokes the current session row (if the cookie is a valid signed session) and clears the cookies.</summary>
    public async Task<SessionPayload?> EndSessionAsync(HttpContext http, CancellationToken ct = default)
    {
        var session = tokens.VerifySession(http.Request.Cookies[SessionCookie]);
        try
        {
            if (session is not null && Js.IsUuid(session.Sid))
                await db.ExecuteAsync("UPDATE user_sessions SET revoked_at = SYSUTCDATETIME() WHERE id = @sid AND revoked_at IS NULL",
                    [SqlDb.Guid("@sid", session.Sid)], ct);
        }
        finally
        {
            ClearCookies(http.Response);
        }
        return session;
    }

    public void ClearCookies(HttpResponse response)
    {
        var expired = new CookieOptions { Path = "/", HttpOnly = true, Secure = config.CookieSecure }; // NOSONAR S2092 — same switch as the session cookie
        response.Cookies.Delete(SessionCookie, expired);
        foreach (var name in LegacyCookies) response.Cookies.Delete(name, expired);
    }
}


