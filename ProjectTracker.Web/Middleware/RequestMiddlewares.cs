using System.Text.RegularExpressions;
using ProjectTracker.Web.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTracker.Web.Middleware;

/// <summary>
/// Cross-site request protection for the cookie-authenticated API. Cookies are SameSite=Lax already; this adds an
/// Origin check on state-changing requests, so it works without changing the UI's fetch calls (no token to send).
/// Requests without an Origin header (scripts, tests, same-origin GET navigation) are not browser cross-site posts.
/// </summary>
public sealed class OriginCheckMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> Unsafe = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (Unsafe.Contains(context.Request.Method) && context.Request.Path.StartsWithSegments("/api")
            && context.Request.Headers.Origin.FirstOrDefault() is { Length: > 0 } origin)
        {
            var allowedHost = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? context.Request.Host.Value;
            var sameOrigin = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && string.Equals(uri.Authority, allowedHost, StringComparison.OrdinalIgnoreCase);
            if (!sameOrigin)
            {
                await ApiResults.Error("طلب غير مصرّح به من مصدر آخر", 403).ExecuteResultAsync(new ActionContext { HttpContext = context });
                return;
            }
        }
        await next(context);
    }
}

/// <summary>
/// The exported page files (*.html) are served only through PagesController, which checks the session and the role.
/// A direct request for the file is redirected to its route instead of reaching the static-file middleware.
/// </summary>
public sealed class PageFileRedirectMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var route = path[..^".html".Length];
            if (route.EndsWith("/index", StringComparison.OrdinalIgnoreCase)) route = route[..^"index".Length];
            context.Response.Redirect((route.Length == 0 ? "/" : route) + context.Request.QueryString);
            return Task.CompletedTask;
        }
        return next(context);
    }
}

/// <summary>Baseline response headers that do not affect how any page renders.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers.XFrameOptions = "SAMEORIGIN";
        return next(context);
    }
}

/// <summary>
/// Serves the Next.js "flight" payloads (*.txt) that the compiled UI fetches for client-side navigation.
/// The exported files exist once per route, with a placeholder for dynamic segments, so real project ids
/// are mapped to the placeholder and the dotted segment names to their directory layout. These files are
/// public static assets (the original proxy excluded *.txt from authentication too).
/// </summary>
public sealed partial class FlightPayloadMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex Guid();

    [GeneratedRegex(@"^(.*/)?__next\.([^./]+)\.(.+)\.txt$")]
    private static partial Regex Segment();

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (HttpMethods.IsGet(context.Request.Method) && path.EndsWith(".txt", StringComparison.Ordinal) && !path.Contains(".."))
        {
            var normalized = Guid().Replace(path, "_");
            var candidates = new List<string>();
            var m = Segment().Match(normalized);
            if (m.Success) candidates.Add($"{m.Groups[1].Value}__next.{m.Groups[2].Value}/{m.Groups[3].Value.Replace('.', '/')}.txt");
            candidates.Add(normalized);

            foreach (var candidate in candidates)
            {
                var file = environment.WebRootFileProvider.GetFileInfo(candidate);
                if (file.Exists && file.PhysicalPath is not null)
                {
                    context.Response.ContentType = "text/x-component";
                    await context.Response.SendFileAsync(file, context.RequestAborted);
                    return;
                }
            }
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(context);
    }
}
