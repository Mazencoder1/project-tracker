using System.Text.Json;
using ProjectTracker.Web.Data;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTracker.Web.Helpers;

/// <summary>Response builders that keep the exact JSON contract of the original API.</summary>
public static class ApiResults
{
    public static JsonResult Json(object? value, int status = 200) => new(value) { StatusCode = status };

    public static JsonResult Error(string message, int status) => Json(new { error = message }, status);

    public static JsonResult BadBody() => Error("بيانات الطلب غير صالحة", 400);

    public static JsonResult ValidationFailed(Dictionary<string, string> errors) =>
        Json(new { error = "تحقق من الحقول المدخلة", errors }, 400);

    public static JsonResult Unauthorized() => Error("يجب تسجيل الدخول أولًا", 401);

    public static JsonResult AdminOnly() => Error("هذا الإجراء متاح لمدير النظام فقط", 403);

    /// <summary>
    /// Never leaks exception text (database names, SQL) to the client: details go to the server log and the
    /// user gets a generic message. Input the database rejects is reported as 400, constraint conflicts as 409.
    /// </summary>
    public static JsonResult ServerError(Exception error, ILogger logger, string context = "api")
    {
        if (error is JsonException) return BadBody();
        if (error is Microsoft.AspNetCore.Http.BadHttpRequestException { StatusCode: 413 })
            return Error("حجم الطلب أكبر من الحد المسموح.", 413);
        if (error is FormatException or OverflowException)
            return Error("بيانات الطلب غير صالحة — تحقّق من المعرّفات والتواريخ المرسلة.", 400);

        var number = SqlDb.SqlErrorNumber(error);
        if (number is 2628 or 8152)
            return Error("إحدى القيم المدخلة أطول من الحد المسموح. اختصرها ثم أعد المحاولة.", 400);

        // 547 covers both foreign-key and CHECK violations; they need different messages.
        if (number == 547)
        {
            if (error.ToString().Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError("[{Context}] CHECK constraint violation: {Message}", context, error.Message);
                return Error("إحدى القيم المدخلة غير مقبولة في قاعدة البيانات. راجع الحقول المرسلة، وإن تكرّر الخطأ فأبلغ مسؤول النظام (قد تكون ترحيلات قاعدة البيانات ناقصة).", 400);
            }
            return Error("لا يمكن تنفيذ العملية لوجود بيانات مرتبطة بهذا العنصر. يمكنك إلغاؤه بدل حذفه.", 409);
        }

        logger.LogError(error, "[{Context}] unhandled error", context);
        return Error("حدث خطأ في الخادم. أعد المحاولة بعد قليل.", 500);
    }
}
