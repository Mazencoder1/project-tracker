using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using Microsoft.Data.SqlClient;

namespace ProjectTracker.Web.Services;

/// <summary>Pure OTP rules (port of lib/otpRules.ts).</summary>
public static partial class OtpRules
{
    public const int Length = 6;
    public const int TtlSec = 5 * 60;
    public const int MaxAttempts = 5;
    public const int MaxResends = 3;
    public const int ResendCooldownSec = 60;
    public const int TicketTtlSec = 15 * 60;

    [GeneratedRegex(@"^\d{6}$")] private static partial Regex CodeRegex();
    [GeneratedRegex(@"\D")] private static partial Regex NonDigit();
    [GeneratedRegex(@"^5\d{8}$")] private static partial Regex SaudiMobile();

    public static bool IsValidCodeFormat(string? code) => code is not null && CodeRegex().IsMatch(code.Trim());

    /// <summary>Any accepted Saudi mobile notation → 9665XXXXXXXX, or null (the account then signs in without OTP).</summary>
    public static string? NormalizeMobile(string? raw)
    {
        if (raw is null) return null;
        var digits = NonDigit().Replace(Js.NormalizeDigits(raw), "");
        if (digits.Length == 0) return null;
        if (digits.StartsWith("00966", StringComparison.Ordinal)) digits = digits[5..];
        else if (digits.StartsWith("966", StringComparison.Ordinal)) digits = digits[3..];
        else if (digits.StartsWith('0')) digits = digits[1..];
        return SaudiMobile().IsMatch(digits) ? $"966{digits}" : null;
    }

    public static string MobileTail(string normalized) => normalized[^4..];

    public static string Message(string code) =>
        $"رمز الدخول إلى منصة متابعة المشاريع: {code}\nصالح {TtlSec / 60} دقائق. لا تشاركه مع أحد.";

    public enum State { Ok, Expired, Used, Locked }

    public sealed record Row(DateTime ExpiresAt, int Attempts, DateTime? ConsumedAt, int Resends, DateTime LastSentAt);

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    /// <summary>Consumed, then expired, then locked — all shown to the user with one message.</summary>
    public static State GetState(Row row, DateTime? now = null)
    {
        if (row.ConsumedAt is not null) return State.Used;
        if (Utc(row.ExpiresAt) <= (now ?? DateTime.UtcNow)) return State.Expired;
        if (row.Attempts >= MaxAttempts) return State.Locked;
        return State.Ok;
    }

    public static int AttemptsLeft(int attempts) => Math.Max(0, MaxAttempts - attempts);

    public readonly record struct ResendCheck(bool Ok, string? Reason = null, int WaitSec = 0);

    public static ResendCheck CanResend(Row row, DateTime? now = null)
    {
        if (row.Resends >= MaxResends) return new(false, "too_many");
        var elapsed = (int)Math.Floor(((now ?? DateTime.UtcNow) - Utc(row.LastSentAt)).TotalSeconds);
        return elapsed < ResendCooldownSec ? new(false, "cooldown", ResendCooldownSec - elapsed) : new(true);
    }
}

/// <summary>SMS one-time code at sign-in (port of lib/otp.ts).</summary>
public sealed class OtpService(SqlDb db, SessionTokens tokens, ISmsService sms, AppConfig config, ILogger<OtpService> logger)
{
    public const string Cookie = "tracker_otp";
    private const string RestartMessage = "انتهت صلاحية رمز التحقق أو لم يعد صالحًا. أعد تسجيل الدخول للحصول على رمز جديد.";

    private sealed record TicketPayload(
        [property: JsonPropertyName("oid")] string Oid,
        [property: JsonPropertyName("uid")] string Uid,
        [property: JsonPropertyName("exp")] long Exp);

    public sealed record OtpUser(string Id, string Role, string Name, string? MobileNo, string? Email = null);

    public abstract record StartResult
    {
        public sealed record NotRequired : StartResult;
        public sealed record Challenge(string Ticket, string MobileTail, int ExpiresInSec) : StartResult;
        public sealed record SendFailed(string Message) : StartResult;
    }

    public bool IsSmsConfigured => sms.IsConfigured;

    private CookieOptions CookieOptions(int maxAge) => new() // NOSONAR S2092 — Secure is configurable, see SessionService.Options
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = config.CookieSecure,
        Path = "/",
        MaxAge = TimeSpan.FromSeconds(maxAge),
    };

    public void SetCookie(HttpResponse response, string ticket) => response.Cookies.Append(Cookie, ticket, CookieOptions(OtpRules.TicketTtlSec));

    public void ClearCookie(HttpResponse response) => response.Cookies.Append(Cookie, "", CookieOptions(0));

    /// <summary>The code is hashed with the session secret as a pepper, so reading the table never reveals a code.</summary>
    private byte[] HashCode(string code, string userId) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{code}:{config.SessionSecret ?? ""}"));

    private static string GenerateCode() =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, OtpRules.Length)).ToString().PadLeft(OtpRules.Length, '0');

    /// <summary>Starts the challenge after identity is proven. Fail-closed: if the SMS cannot be sent there is no session.</summary>
    public async Task<StartResult> StartChallengeAsync(OtpUser user, string via, CancellationToken ct = default)
    {
        if (!config.OtpEnabled) return new StartResult.NotRequired();
        if (via == LoginMethod.Password && user.Email is { } email && config.OtpSkipEmails.Contains(email))
        {
            logger.LogWarning("OTP skipped for {Email} (Otp:SkipEmails).", email);
            return new StartResult.NotRequired();
        }

        var mobile = OtpRules.NormalizeMobile(user.MobileNo);
        if (mobile is null) return new StartResult.NotRequired();

        if (!IsSmsConfigured)
        {
            logger.LogWarning("SMS service not configured (SMS_URL/SMS_WS_KEY) — account {UserId} signed in without an OTP.", user.Id);
            return new StartResult.NotRequired();
        }

        var code = GenerateCode();
        var tail = OtpRules.MobileTail(mobile);
        var sets = await db.QueryMultiAsync(
            """
            DELETE FROM login_otp WHERE expires_at < DATEADD(hour, -1, SYSUTCDATETIME());
            DELETE FROM login_otp WHERE user_id = @user_id AND consumed_at IS NULL;
            INSERT INTO login_otp (user_id, code_hash, mobile_tail, via, expires_at)
            OUTPUT INSERTED.id
            VALUES (@user_id, @code_hash, @mobile_tail, @via, DATEADD(second, @ttl, SYSUTCDATETIME()));
            """,
            [
                SqlDb.Guid("@user_id", user.Id),
                new SqlParameter("@code_hash", SqlDbType.VarBinary, 32) { Value = HashCode(code, user.Id) },
                SqlDb.Text("@mobile_tail", tail, 4),
                SqlDb.Text("@via", via, 20),
                SqlDb.Int("@ttl", OtpRules.TtlSec),
            ], ct);
        var otpId = sets[^1][0].Str("id")!;

        var sent = await sms.SendAsync(mobile, OtpRules.Message(code), ct);
        if (!sent.Ok)
        {
            await db.ExecuteAsync("DELETE FROM login_otp WHERE id = @id", [SqlDb.Guid("@id", otpId)], ct);
            logger.LogError("Could not send the OTP to account {UserId}: {Message}", user.Id, sent.Message);
            return new StartResult.SendFailed("تعذّر إرسال رمز التحقق إلى جوالك حاليًا. أعد المحاولة بعد قليل، وإن تكرر تواصل مع مسؤول النظام.");
        }

        var ticket = tokens.Sign(new TicketPayload(otpId, user.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + OtpRules.TicketTtlSec));
        return new StartResult.Challenge(ticket, tail, OtpRules.TtlSec);
    }

    private sealed record Pending(TicketPayload Ticket, string OtpId, byte[] CodeHash, string? MobileTail, string Via, OtpRules.Row Row,
        string UserId, string Role, string FullName, string? MobileNo);

    private async Task<Pending?> LoadPendingAsync(string? ticketValue, CancellationToken ct)
    {
        var ticket = tokens.Verify<TicketPayload>(ticketValue);
        if (ticket is null || string.IsNullOrEmpty(ticket.Oid) || string.IsNullOrEmpty(ticket.Uid)) return null;
        if (ticket.Exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
        if (!Js.IsUuid(ticket.Oid) || !Js.IsUuid(ticket.Uid)) return null;

        var row = await db.FirstAsync(
            """
            SELECT o.id, o.user_id, o.code_hash, o.mobile_tail, o.via, o.expires_at, o.attempts,
                   o.resends, o.last_sent_at, o.consumed_at,
                   p.role, p.full_name, p.mobile_no, ISNULL(p.status, 'active') AS status
            FROM login_otp o
            JOIN profiles p ON p.id = o.user_id
            WHERE o.id = @id AND o.user_id = @user_id
            """,
            [SqlDb.Guid("@id", ticket.Oid), SqlDb.Guid("@user_id", ticket.Uid)], ct);
        if (row is null || row.Str("status") != "active") return null;

        return new Pending(ticket, row.Str("id")!, (byte[])row["code_hash"]!, row.Str("mobile_tail") is { Length: > 0 } t ? t : null,
            row.Str("via") ?? "",
            new OtpRules.Row((DateTime)row["expires_at"]!, row.Int("attempts") ?? 0, row["consumed_at"] as DateTime?, row.Int("resends") ?? 0, (DateTime)row["last_sent_at"]!),
            row.Str("user_id")!, row.Str("role") ?? "", row.Str("full_name") ?? "", row.Str("mobile_no") is { Length: > 0 } m ? m : null);
    }

    public abstract record VerifyResult
    {
        public sealed record Success(string UserId, string Role, string Name, string Via) : VerifyResult;
        public sealed record Failure(string Reason, string Message, int? AttemptsLeft = null) : VerifyResult;
    }

    public async Task<VerifyResult> VerifyAsync(string? ticketValue, string code, CancellationToken ct = default)
    {
        var pending = await LoadPendingAsync(ticketValue, ct);
        if (pending is null) return new VerifyResult.Failure("invalid_ticket", RestartMessage);
        if (OtpRules.GetState(pending.Row) != OtpRules.State.Ok) return new VerifyResult.Failure("expired", RestartMessage);

        var given = HashCode(code.Trim(), pending.UserId);
        var match = pending.CodeHash.Length == given.Length && CryptographicOperations.FixedTimeEquals(pending.CodeHash, given);

        if (!match)
        {
            var updated = await db.FirstAsync("UPDATE login_otp SET attempts = attempts + 1 OUTPUT INSERTED.attempts WHERE id = @id",
                [SqlDb.Guid("@id", pending.OtpId)], ct);
            var attempts = updated?.Int("attempts") ?? pending.Row.Attempts + 1;
            var left = OtpRules.AttemptsLeft(attempts);
            return new VerifyResult.Failure("wrong_code",
                left > 0 ? $"رمز التحقق غير صحيح. تبقّت {ArabicCount.Format(left, "attempt")}." : RestartMessage, left);
        }

        var consumed = await db.ExecuteAsync("UPDATE login_otp SET consumed_at = SYSUTCDATETIME() WHERE id = @id AND consumed_at IS NULL",
            [SqlDb.Guid("@id", pending.OtpId)], ct);
        if (consumed == 0) return new VerifyResult.Failure("expired", RestartMessage);

        return new VerifyResult.Success(pending.UserId, pending.Role, pending.FullName, LoginMethod.Password);
    }

    public abstract record ResendResult
    {
        public sealed record Success(string MobileTail, int ExpiresInSec) : ResendResult;
        public sealed record Failure(string Message, int? WaitSec = null) : ResendResult;
    }

    public async Task<ResendResult> ResendAsync(string? ticketValue, CancellationToken ct = default)
    {
        var pending = await LoadPendingAsync(ticketValue, ct);
        if (pending is null || OtpRules.GetState(pending.Row) != OtpRules.State.Ok) return new ResendResult.Failure(RestartMessage);

        var allowed = OtpRules.CanResend(pending.Row);
        if (!allowed.Ok)
        {
            return allowed.Reason == "cooldown"
                ? new ResendResult.Failure($"انتظر {allowed.WaitSec} ثانية قبل طلب رمز جديد.", allowed.WaitSec)
                : new ResendResult.Failure("تجاوزت عدد مرات إعادة الإرسال. أعد تسجيل الدخول للحصول على رمز جديد.");
        }

        var mobile = OtpRules.NormalizeMobile(pending.MobileNo);
        if (mobile is null) return new ResendResult.Failure(RestartMessage);

        var code = GenerateCode();
        await db.ExecuteAsync(
            """
            UPDATE login_otp
            SET code_hash = @code_hash,
                attempts = 0,
                resends = resends + 1,
                last_sent_at = SYSUTCDATETIME(),
                expires_at = DATEADD(second, @ttl, SYSUTCDATETIME())
            WHERE id = @id AND consumed_at IS NULL
            """,
            [
                SqlDb.Guid("@id", pending.OtpId),
                new SqlParameter("@code_hash", SqlDbType.VarBinary, 32) { Value = HashCode(code, pending.UserId) },
                SqlDb.Int("@ttl", OtpRules.TtlSec),
            ], ct);

        var sent = await sms.SendAsync(mobile, OtpRules.Message(code), ct);
        if (!sent.Ok)
        {
            logger.LogError("Could not resend the OTP to account {UserId}: {Message}", pending.UserId, sent.Message);
            return new ResendResult.Failure("تعذّر إرسال رمز التحقق حاليًا. أعد المحاولة بعد قليل.");
        }
        return new ResendResult.Success(OtpRules.MobileTail(mobile), OtpRules.TtlSec);
    }

    /// <summary>Display data for the pending code screen (last four digits only).</summary>
    public sealed record PendingInfo(string? MobileTail);

    public async Task<PendingInfo?> PendingInfoAsync(string? ticketValue, CancellationToken ct = default)
    {
        var pending = await LoadPendingAsync(ticketValue, ct);
        return pending is not null && OtpRules.GetState(pending.Row) == OtpRules.State.Ok ? new PendingInfo(pending.MobileTail) : null;
    }
}
