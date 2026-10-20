using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Services;
using ProjectTracker.Web.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ProjectTracker.Web.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    SqlDb db,
    SessionService sessions,
    OtpService otp,
    ILogger<AuthController> logger,
    LoginThrottle throttle) : ApiControllerBase
{
    private sealed record LoginRow(string Id, string FullName, string Role, string? Status, string? PasswordHash, string? MobileNo);

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login()
    {
        var body = await JsonBody.ReadAsync(Request);
        var email = body?.String("email")?.Trim() ?? "";
        var password = body?.String("password") ?? "";
        if (email.Length == 0 || password.Length == 0)
            return Error("البريد الإلكتروني وكلمة المرور مطلوبان", 400);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (throttle.BlockedMinutes(email, ip) is { } wait)
            return Error($"محاولات دخول خاطئة كثيرة — أُوقف الدخول مؤقتًا. حاول مرة أخرى بعد {wait} دقيقة.", 429);

        var rows = (await db.QueryAsync(
            """
            SELECT id, full_name, role, status, password_hash, mobile_no
            FROM profiles
            WHERE LTRIM(RTRIM(email)) = @email AND ISNULL(status, 'active') <> 'deleted'
            """, [SqlDb.Text("@email", email)]))
            .Select(r => new LoginRow(r.Str("id")!, r.Str("full_name") ?? "", r.Str("role") ?? "", r.Str("status"), r.Str("password_hash"), r.Str("mobile_no")));

        LoginRow? user = null;
        var needsRehash = false;
        foreach (var row in rows)
        {
            var verified = PasswordHasher.Verify(row.PasswordHash, password);
            if (verified.Valid)
            {
                user = row;
                needsRehash = verified.NeedsRehash;
                break;
            }
        }
        if (user is null)
        {
            throttle.RecordFailure(email, ip);
            return Error("البريد أو كلمة المرور غير صحيحة", 401);
        }
        throttle.RecordSuccess(email);
        if (user.Status == "disabled") return Error("تم إيقاف هذا الحساب. تواصل مع مدير النظام لإعادة تفعيله.", 403);

        // Legacy plain-text / transitional hashes are upgraded to scrypt on the first successful login.
        if (needsRehash)
            await db.ExecuteAsync("UPDATE profiles SET password_hash = @password_hash WHERE id = @id",
                [SqlDb.Guid("@id", user.Id), SqlDb.Text("@password_hash", PasswordHasher.Hash(password), 255)]);

        var challenge = await otp.StartChallengeAsync(new OtpService.OtpUser(user.Id, user.Role, user.FullName, user.MobileNo, email), LoginMethod.Password);
        switch (challenge)
        {
            case OtpService.StartResult.SendFailed failed:
                return Error(failed.Message, 503);
            case OtpService.StartResult.Challenge c:
                otp.SetCookie(Response, c.Ticket);
                return Json(new { otpRequired = true, mobileTail = c.MobileTail, expiresInSec = c.ExpiresInSec });
        }

        await sessions.StartSessionAsync(Response, user.Id, user.Role, user.FullName);
        return Json(new { role = user.Role, name = user.FullName });
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            await sessions.EndSessionAsync(HttpContext);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not revoke the session on the server");
            return Error("تم تسجيل خروجك من هذا المتصفح، لكن تعذّر إنهاء الجلسة على الخادم.", 500);
        }

        Response.Headers.CacheControl = "no-store";
        return Json(new { success = true });
    }

    [HttpGet("me")]
    public IActionResult CurrentSession()
    {
        var id = Request.Cookies["user_id"];
        var role = Request.Cookies["user_role"];
        var name = Request.Cookies["user_name"];
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(role)) return Error("لا توجد جلسة دخول نشطة", 401);

        Response.Headers.CacheControl = "no-store";
        return Json(new { role, name = name ?? "" });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword()
    {
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        var newPassword = body["newPassword"] is { } np ? JsonBody.ToClr(np) : null;
        var problem = ProjectRules.PasswordProblem(newPassword);
        if (problem is not null) return Error(problem, 400);

        var confirm = body["confirmPassword"] is { } cp ? JsonBody.ToClr(cp) : null;
        if (!Equals(newPassword, confirm)) return Error("كلمة المرور الجديدة غير متطابقة", 400);

        var current = await db.FirstAsync("SELECT password_hash FROM profiles WHERE id = @user_id", [SqlDb.Guid("@user_id", Me.Id)]);
        var verified = current is not null
            ? PasswordHasher.Verify(current.Str("password_hash"), body.String("currentPassword") ?? "")
            : new PasswordHasher.Verification(false, false);
        if (!verified.Valid) return Error("كلمة المرور الحالية غير صحيحة", 400);

        await db.ExecuteAsync("UPDATE profiles SET password_hash = @password_hash WHERE id = @user_id",
            [SqlDb.Guid("@user_id", Me.Id), SqlDb.Text("@password_hash", PasswordHasher.Hash((string)newPassword!), 255)]);

        // Every other session of this account ends; the current one stays.
        await sessions.RevokeUserSessionsAsync(Me.Id, Me.Sid);

        return Json(new { success = true, message = "تم تغيير كلمة المرور بنجاح، وأُنهيت جلساتك في الأجهزة الأخرى." });
    }

    // ── forgot password: a new password is e-mailed to the registered address ──
    /// <summary>
    /// Login page: by the user's request the answer says whether the e-mail is registered in the
    /// system (it used to answer the same for every address); the per-IP limit keeps this from being used to test lists.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(TrackerAuth.ForgotPasswordRateLimit)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromServices] PasswordResetService resets, [FromServices] NotifyTemplates templates)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var body = await JsonBody.ReadAsync(Request);
        var email = body?.String("email")?.Trim() ?? "";
        if (email.Length == 0 || email.Length > 255 || !email.Contains('@'))
            return Json(new { error = "أدخل بريدًا إلكترونيًا صحيحًا", field = "forgot-email" }, 400);

        var loginUrl = $"{templates.AppBaseUrl() ?? $"{Request.Scheme}://{Request.Host}"}/login";
        return await resets.RequestLinkAsync(email, loginUrl) switch
        {
            PasswordResetService.Result.LinkSent sent => Json(new { success = true, message = $"أُرسل رابط تأكيد إلى {sent.Email}. افتحه خلال {PasswordResetService.LinkMinutes} دقيقة لتصلك كلمة مرور جديدة — كلمة مرورك الحالية تبقى صالحة حتى ذلك الحين." }),
            PasswordResetService.Result.NotRegistered => Json(new { error = "هذا البريد الإلكتروني غير مسجّل في النظام. تأكّد من كتابته كما هو مسجّل في حسابك.", field = "forgot-email" }, 404),
            PasswordResetService.Result.Disabled => Error("هذا الحساب موقوف. تواصل مع مدير النظام لإعادة تفعيله.", 403),
            PasswordResetService.Result.TooSoon wait => Error($"أُرسل طلب استعادة لهذا البريد قبل قليل — تحقّق من بريدك أو حاول بعد {wait.WaitMinutes} دقيقة.", 429),
            PasswordResetService.Result.SendFailed => Error("تعذّر إرسال البريد الإلكتروني، ولم تتغير كلمة مرورك. حاول لاحقًا.", 503),
            _ => Error("لا يمكن استعادة كلمة المرور لهذا الحساب بالبريد. تواصل مع مدير النظام.", 400),
        };
    }

    /// <summary>Login page, step 2 of "forgot password": the signed link from the e-mail creates and e-mails the new password.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(TrackerAuth.ForgotPasswordRateLimit)]
    [HttpPost("forgot-password/confirm")]
    public async Task<IActionResult> ConfirmForgotPassword([FromServices] PasswordResetService resets)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var body = await JsonBody.ReadAsync(Request);
        return await resets.ConfirmLinkAsync(body?.String("token")) switch
        {
            PasswordResetService.Result.Sent sent => Json(new { success = true, message = $"أُرسلت كلمة مرور جديدة إلى {MaskEmail(sent.Email)}. تحقّق من صندوق الوارد والبريد غير المرغوب، ثم سجّل الدخول بها." }),
            PasswordResetService.Result.Disabled => Error("هذا الحساب موقوف. تواصل مع مدير النظام لإعادة تفعيله.", 403),
            PasswordResetService.Result.SendFailed => Error("تعذّر إرسال البريد الإلكتروني، ولم تتغير كلمة مرورك. أعد فتح الرابط لاحقًا.", 503),
            PasswordResetService.Result.InvalidLink => Error("رابط الاستعادة غير صالح أو انتهت مدته أو استُخدم من قبل. اطلب رابطًا جديدًا من «نسيت كلمة المرور؟».", 410),
            _ => Error("لا يمكن استعادة كلمة المرور لهذا الحساب بالبريد. تواصل مع مدير النظام.", 400),
        };
    }

    /// <summary>Settings page: the signed-in user asks for a new password on their own registered e-mail.</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetOwnPassword([FromServices] PasswordResetService resets)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return await resets.ResetOwnAsync(Me.Id, Me.Sid) switch
        {
            PasswordResetService.Result.Sent sent => Json(new { success = true, email = MaskEmail(sent.Email), message = $"أُرسلت كلمة مرور جديدة إلى {MaskEmail(sent.Email)}، وأُنهيت جلساتك في الأجهزة الأخرى." }),
            PasswordResetService.Result.TooSoon wait => Error($"طُلبت كلمة مرور جديدة قبل قليل — حاول مرة أخرى بعد {wait.WaitMinutes} دقيقة.", 429),
            PasswordResetService.Result.SendFailed => Error("تعذّر إرسال البريد الإلكتروني، ولم تتغير كلمة مرورك. حاول لاحقًا.", 503),
            _ => Error("لا يوجد بريد إلكتروني مسجّل لحسابك أو أن حسابك لا يستخدم كلمة مرور. تواصل مع مدير النظام.", 400),
        };
    }

    /// <summary>m****@example.com — enough to recognise the mailbox without printing it.</summary>
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}{new string('*', Math.Min(at - 1, 6))}{email[at..]}";
    }

    // ── one-time code after identity is proven ───────────────────────────────
    [AllowAnonymous]
    [HttpPost("otp/verify")]
    public async Task<IActionResult> OtpVerify()
    {
        var body = await JsonBody.ReadAsync(Request);
        var code = body?.String("code")?.Trim() ?? "";
        if (!OtpRules.IsValidCodeFormat(code))
            return Json(new { error = $"أدخل رمز التحقق المكوّن من {OtpRules.Length} أرقام", field = "otp-code" }, 400);

        var result = await otp.VerifyAsync(Request.Cookies[OtpService.Cookie], code);
        if (result is OtpService.VerifyResult.Failure failure)
        {
            var wrong = failure.Reason == "wrong_code";
            if (!wrong) otp.ClearCookie(Response);
            var payload = new Dictionary<string, object?> { ["error"] = failure.Message };
            if (failure.AttemptsLeft is not null) payload["attemptsLeft"] = failure.AttemptsLeft;
            payload["restart"] = !wrong;
            return Json(payload, wrong ? 401 : 410);
        }

        var ok = (OtpService.VerifyResult.Success)result;
        await sessions.StartSessionAsync(Response, ok.UserId, ok.Role, ok.Name, ok.Via);
        otp.ClearCookie(Response);
        return Json(new { role = ok.Role, name = ok.Name });
    }

    /// <summary>Whether a sign-in code is pending for this browser (last four digits of the mobile only).</summary>
    [AllowAnonymous]
    [HttpGet("otp/resend")]
    public async Task<IActionResult> OtpPending()
    {
        var info = await otp.PendingInfoAsync(Request.Cookies[OtpService.Cookie]);
        Response.Headers.CacheControl = "no-store";
        return Json(new { pending = info is not null, mobileTail = info?.MobileTail });
    }

    [AllowAnonymous]
    [HttpPost("otp/resend")]
    public async Task<IActionResult> OtpResend()
    {
        var result = await otp.ResendAsync(Request.Cookies[OtpService.Cookie]);
        if (result is OtpService.ResendResult.Failure failure)
        {
            var restart = failure.WaitSec is null;
            if (restart) otp.ClearCookie(Response);
            var payload = new Dictionary<string, object?> { ["error"] = failure.Message };
            if (failure.WaitSec is not null) payload["waitSec"] = failure.WaitSec;
            payload["restart"] = restart;
            return Json(payload, 429);
        }

        var ok = (OtpService.ResendResult.Success)result;
        return Json(new { mobileTail = ok.MobileTail, expiresInSec = ok.ExpiresInSec });
    }
}
