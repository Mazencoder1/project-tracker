using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;

namespace ProjectTracker.Web.Services;

/// <summary>
/// "Forgot password": a new random password is generated and e-mailed to the account's registered address. The stored
/// hash is replaced only after the e-mail was accepted by the relay, so a failed send never locks anyone out.
/// The anonymous path first e-mails a signed confirmation link; nothing changes until the mailbox owner opens it.
/// </summary>
public sealed class PasswordResetService(
    SqlDb db, IMailService mail, SessionService sessions, SessionTokens tokens, AuditService audit, ILogger<PasswordResetService> logger)
{
    public const int LinkMinutes = 30;
    private const string TokenPurpose = "pwreset";

    private sealed record ResetToken(
        [property: JsonPropertyName("purpose")] string? Purpose,
        [property: JsonPropertyName("uid")] string? UserId,
        [property: JsonPropertyName("fp")] string? Fp,
        [property: JsonPropertyName("exp")] long Exp);

    /// <summary>One reset per account in this window (checked against audit_log, so it holds across restarts and instances).</summary>
    public const int CooldownMinutes = 5;
    private const int GeneratedLength = 12;
    private const string EntityType = "password_reset";

    public abstract record Result
    {
        public sealed record Sent(string Email) : Result;
        public sealed record NotEligible : Result;
        public sealed record NotRegistered : Result;
        public sealed record Disabled : Result;
        public sealed record TooSoon(int WaitMinutes) : Result;
        public sealed record SendFailed : Result;
        public sealed record LinkSent(string Email) : Result;
        public sealed record InvalidLink : Result;
    }

    private sealed record Account(string Id, string FullName, string Email);

    /// <summary>
    /// Anonymous path, step 1: the e-mail must match the one registered in the system (by the user's request the answer says
    /// so explicitly). Only an active account that already signs in with a password qualifies. Sends a confirmation link only.
    /// </summary>
    public async Task<Result> RequestLinkAsync(string email, string loginUrl, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            """
            SELECT id, full_name, email, status, password_hash FROM profiles
            WHERE LTRIM(RTRIM(email)) = @email AND ISNULL(status, 'active') <> 'deleted'
            """, [SqlDb.Text("@email", email.Trim())], ct);
        if (rows.Count == 0) return new Result.NotRegistered();
        if (rows.All(r => r.Str("status") == "disabled")) return new Result.Disabled();
        // Note: بريد مكرر بين حسابين لا يُعاد تعيينه — لا يمكن معرفة أي الحسابين يقصده الطالب.
        if (rows.Count != 1 || rows[0].Str("password_hash") is not { } hash) return new Result.NotEligible();
        var account = new Account(rows[0].Str("id")!, rows[0].Str("full_name") ?? "", rows[0].Str(Fields.Email)!.Trim());

        if (await WaitMinutesAsync(account.Id) is { } wait) return new Result.TooSoon(wait);

        var token = tokens.Sign(new ResetToken(TokenPurpose, account.Id, Fingerprint(hash),
            DateTimeOffset.UtcNow.AddMinutes(LinkMinutes).ToUnixTimeSeconds()));
        var link = $"{loginUrl}?reset_token={Uri.EscapeDataString(token)}";
        var sent = await mail.SendAsync(new MailRequest([account.Email], "تأكيد استعادة كلمة المرور — منصة متابعة المشاريع", LinkBody(account.FullName, link)), ct);
        if (!sent.Ok)
        {
            logger.LogWarning("Password reset link for account {UserId} was not sent: {Reason}", account.Id, sent.Reason);
            return new Result.SendFailed();
        }
        // Note: السجل بلا الرابط؛ يُستخدم أيضًا لفترة الانتظار بين طلبين.
        await audit.WriteAsync(null, "create", EntityType, account.Id, null, new { to = account.Email, via = "login", step = "link_sent" });
        return new Result.LinkSent(account.Email);
    }

    /// <summary>Anonymous path, step 2: a valid, unexpired and unused link creates the new password and e-mails it.</summary>
    public async Task<Result> ConfirmLinkAsync(string? token, CancellationToken ct = default)
    {
        var payload = tokens.Verify<ResetToken>(token);
        if (payload is not { Purpose: TokenPurpose } || !Js.IsUuid(payload.UserId) || payload.Exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return new Result.InvalidLink();

        var row = await db.FirstAsync(
            "SELECT id, full_name, email, status, password_hash FROM profiles WHERE id = @id AND ISNULL(status, 'active') <> 'deleted'",
            [SqlDb.Guid("@id", payload.UserId)], ct);
        if (row is null || row.Str("password_hash") is not { } hash) return new Result.InvalidLink();
        if (row.Str("status") == "disabled") return new Result.Disabled();
        // A changed password (this link already used, or changed by other means) invalidates the link.
        var expected = Encoding.ASCII.GetBytes(Fingerprint(hash));
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(payload.Fp ?? ""))) return new Result.InvalidLink();
        if (string.IsNullOrWhiteSpace(row.Str(Fields.Email))) return new Result.NotEligible();

        return await ResetAsync(new Account(row.Str("id")!, row.Str("full_name") ?? "", row.Str(Fields.Email)!.Trim()), keepSid: null, checkCooldown: false, ct);
    }

    private static string Fingerprint(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..32];

    private async Task<int?> WaitMinutesAsync(string userId)
    {
        var last = await db.FirstAsync(
            """
            SELECT TOP 1 DATEDIFF(second, created_at, GETDATE()) AS age
            FROM audit_log WHERE entity_type = @type AND entity_id = @id ORDER BY created_at DESC
            """, [SqlDb.Text("@type", EntityType, 50), SqlDb.Guid("@id", userId)]);
        return last?.Int("age") is { } age && age < CooldownMinutes * 60 ? (int)Math.Ceiling((CooldownMinutes * 60 - age) / 60.0) : null;
    }

    /// <summary>Signed-in path (settings page): the user's own account; the current session stays open.</summary>
    public async Task<Result> ResetOwnAsync(string userId, string? currentSid, CancellationToken ct = default)
    {
        var row = await db.FirstAsync(
            "SELECT id, full_name, email, password_hash FROM profiles WHERE id = @id AND ISNULL(status, 'active') = 'active'",
            [SqlDb.Guid("@id", userId)], ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Str(Fields.Email)) || row.Str("password_hash") is null) return new Result.NotEligible();
        return await ResetAsync(new Account(row.Str("id")!, row.Str("full_name") ?? "", row.Str(Fields.Email)!.Trim()), currentSid, checkCooldown: true, ct);
    }

    private async Task<Result> ResetAsync(Account account, string? keepSid, bool checkCooldown, CancellationToken ct)
    {
        if (checkCooldown && await WaitMinutesAsync(account.Id) is { } wait) return new Result.TooSoon(wait);

        var password = Generate();
        var sent = await mail.SendAsync(new MailRequest([account.Email], "كلمة المرور الجديدة — منصة متابعة المشاريع", Body(account.FullName, password)), ct);
        if (!sent.Ok)
        {
            logger.LogWarning("Password reset e-mail for account {UserId} was not sent: {Reason}", account.Id, sent.Reason);
            return new Result.SendFailed();
        }

        await db.ExecuteAsync("UPDATE profiles SET password_hash = @password_hash WHERE id = @id",
            [SqlDb.Guid("@id", account.Id), SqlDb.Text("@password_hash", PasswordHasher.Hash(password), 255)], CancellationToken.None);
        await sessions.RevokeUserSessionsAsync(account.Id, keepSid, ct);
        // Note: السجل بلا كلمة المرور؛ يُستخدم أيضًا لفترة الانتظار بين طلبين.
        await audit.WriteAsync(keepSid is null ? null : account.Id, "update", EntityType, account.Id, null, new { to = account.Email, via = keepSid is null ? "login" : "settings" });
        return new Result.Sent(account.Email);
    }

    /// <summary>Upper, lower, digit and symbol, without look-alike characters (0/O, 1/l/I).</summary>
    public static string Generate()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnpqrstuvwxyz", digits = "23456789", symbols = "@#$%&*!?";
        const string all = upper + lower + digits + symbols;
        var chars = new char[GeneratedLength];
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (var i = 4; i < chars.Length; i++) chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static string LinkBody(string fullName, string link) =>
        NotifyTemplates.GreetingCard(fullName,
            "<p>وصلنا طلب استعادة كلمة المرور لحسابك في منصة متابعة المشاريع. لإكمال الطلب افتح الرابط التالي ثم اضغط «تأكيد»، فتصلك كلمة مرور جديدة على هذا البريد:</p>"
            + NotifyTemplates.CenteredLinkButton(NotifyTemplates.Escape(link), "تأكيد استعادة كلمة المرور")
            + $"<p>الرابط صالح لمدة {LinkMinutes} دقيقة ولمرة واحدة. كلمة مرورك الحالية تبقى صالحة ما لم تؤكد الطلب.</p>"
            + NotifyTemplates.FinePrint("إن لم تطلب استعادة كلمة المرور فتجاهل هذه الرسالة."));

    private static string Body(string fullName, string password) =>
        NotifyTemplates.GreetingCard(fullName,
            "<p>بناءً على طلب استعادة كلمة المرور، أُنشئت لحسابك في منصة متابعة المشاريع كلمة مرور جديدة:</p>"
            + $"<p dir=\"ltr\" style=\"font-family:Consolas,monospace;font-size:20px;font-weight:bold;letter-spacing:2px;text-align:center;background:#f3f4f6;padding:12px;border-radius:6px\">{NotifyTemplates.Escape(password)}</p>"
            + "<p>كلمة المرور السابقة لم تعد صالحة، وأُنهيت جلساتك المفتوحة. ننصحك بتغيير كلمة المرور هذه من صفحة «الإعدادات» بعد تسجيل الدخول.</p>"
            + NotifyTemplates.FinePrint("إن لم تطلب استعادة كلمة المرور فتواصل مع مدير النظام فورًا."));
}
