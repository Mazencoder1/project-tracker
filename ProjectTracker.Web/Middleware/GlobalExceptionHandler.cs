using System.Text.Encodings.Web;
using System.Text.Json;

namespace ProjectTracker.Web.Middleware;

/// <summary>Unhandled errors never reach the user as raw exceptions: JSON for /api, a small HTML page elsewhere.</summary>
public static class GlobalExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task HandleAsync(HttpContext context)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled");
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        // Malformed request content (bad JSON / invalid Unicode) or an oversized body is the caller's fault: 400/413, not a server error.
        var status = feature?.Error switch
        {
            JsonException => StatusCodes.Status400BadRequest,
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => StatusCodes.Status413PayloadTooLarge,
            _ => StatusCodes.Status500InternalServerError,
        };
        if (feature?.Error is { } error)
        {
            if (status == StatusCodes.Status500InternalServerError) logger.LogError(error, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
            else logger.LogWarning("Rejected request on {Method} {Path}: {Reason}", context.Request.Method, context.Request.Path, error.GetType().Name);
        }
        context.Response.StatusCode = status;
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            var message = status switch
            {
                StatusCodes.Status400BadRequest => "بيانات الطلب غير صالحة",
                StatusCodes.Status413PayloadTooLarge => "حجم الطلب أكبر من الحد المسموح.",
                _ => "حدث خطأ في الخادم. أعد المحاولة بعد قليل.",
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }, JsonOptions), context.RequestAborted);
        }
        else
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("<!doctype html><html lang=\"ar\" dir=\"rtl\"><meta charset=\"utf-8\"><title>خطأ</title><body style=\"font-family:sans-serif;padding:2rem\">حدث خطأ في الخادم. أعد المحاولة بعد قليل.</body></html>", context.RequestAborted);
        }
    }
}
