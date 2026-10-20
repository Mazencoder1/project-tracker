using Microsoft.AspNetCore.Http;

namespace ProjectTracker.Web.Services;

/// <summary>
/// the public address used inside e-mail links. It used to be the fixed Application:BaseUrl
/// (http://intranet-host:3000 / http://localhost:3000), so every link pointed there whatever address the server really had.
/// Order now: (1) Application:BaseUrl / APP_BASE_URL when an administrator fills it in (explicit override);
/// (2) the address of the current signed-in request (an assignment or completion is saved from the browser);
/// (3) the last real address seen in a signed-in request (for the scheduled weekly reminder, which runs without a request);
/// (4) a loopback address only when nothing else was ever seen. Forwarded headers are applied before
/// <see cref="Remember"/> runs, so a reverse proxy's public host is what gets recorded.
/// </summary>
public sealed class PublicUrlResolver(AppConfig config, IHttpContextAccessor http, ILogger<PublicUrlResolver> logger)
{
    private string? lastPublic;
    private string? lastLoopback;

    /// <summary>The configured address (path kept for a virtual directory, no trailing slash), or null.</summary>
    public string? ConfiguredBaseUrl()
    {
        var raw = config.AppBaseUrl;
        if (raw is null) return null;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        logger.LogError("Application:BaseUrl is not a valid http/https address — the address the system is opened with is used instead.");
        return null;
    }

    /// <summary>Called for every incoming request: records the address the system was opened with.</summary>
    public void Remember(HttpRequest request)
    {
        var origin = OriginOf(request);
        if (origin is null) return;
        if (IsLoopback(request.Host.Host)) lastLoopback = origin;
        else lastPublic = origin;
    }

    /// <summary>The address to put in links (no trailing slash), or null when none is known yet.</summary>
    public string? BaseUrl()
    {
        if (ConfiguredBaseUrl() is { } configured) return configured;
        // The current request's Host is trusted only for a signed-in user; an anonymous request (forgot-password)
        // could carry a forged Host and receive a link to another site.
        var context = http.HttpContext;
        var request = context?.User.Identity?.IsAuthenticated == true ? context.Request : null;
        var current = request is null ? null : OriginOf(request);
        if (current is not null && !IsLoopback(request!.Host.Host)) return current;
        return lastPublic ?? current ?? lastLoopback;
    }

    private static string? OriginOf(HttpRequest request) =>
        request.Host.HasValue ? $"{request.Scheme}://{request.Host.Value}{request.PathBase}".TrimEnd('/') : null;

    private static bool IsLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var ip) && System.Net.IPAddress.IsLoopback(ip));
}
