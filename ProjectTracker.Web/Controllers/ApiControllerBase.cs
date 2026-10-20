using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTracker.Web.Controllers;

/// <summary>Shared helpers of the JSON API controllers. Routes, verbs and JSON shapes match the original API exactly.</summary>
[ServiceFilter(typeof(ApiExceptionFilter))]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The signed-in user (routes that reach this code are authenticated by the session handler).</summary>
    protected SessionPayload Me => User.SessionUser();

    protected static JsonResult Json(object? value, int status = 200) => ApiResults.Json(value, status);

    protected static JsonResult Error(string message, int status) => ApiResults.Error(message, status);

    [FromQuery(Name = "page")] public string? PageQuery { get; set; }

    [FromQuery(Name = "pageSize")] public string? PageSizeQuery { get; set; }

    /// <summary>?page and ?pageSize with a ceiling, so nobody can pull a whole table in one request.</summary>
    protected (int Page, int PageSize, int Offset) Paging(int defaultPageSize, int maxPageSize)
    {
        var rawPage = Js.ParseInt(PageQuery ?? "1");
        // The ceiling prevents integer overflow in the offset (a huge page produced a negative OFFSET ⇒ 500).
        var page = rawPage is > 0 ? Math.Min(rawPage.Value, 1_000_000) : 1;
        var rawSize = Js.ParseInt(PageSizeQuery ?? defaultPageSize.ToString());
        var pageSize = rawSize is > 0 ? Math.Min(rawSize.Value, maxPageSize) : defaultPageSize;
        return (page, pageSize, (page - 1) * pageSize);
    }

    protected static object PaginationMeta(int page, int pageSize, int total) =>
        new { page, pageSize, total, totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)) };

    /// <summary>
    /// ?q= as a LIKE pattern ('%text%') with the LIKE wildcards escaped (use with <c>ESCAPE '\'</c>),
    /// or null when absent. Capped at 100 characters.
    /// </summary>
    protected string? SearchPattern()
    {
        var q = Query("q")?.Trim();
        if (string.IsNullOrEmpty(q)) return null;
        if (q.Length > 100) q = q[..100];
        var escaped = q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
        return $"%{escaped}%";
    }

    /// <summary>
    /// ?sort= and ?dir= through a whitelist of SQL expressions — user text never reaches ORDER BY.
    /// Unknown or missing keys fall back to <paramref name="fallback"/> (which carries its own direction).
    /// </summary>
    protected string OrderBy(IReadOnlyDictionary<string, string> allowed, string fallback)
    {
        var key = Query("sort");
        if (key is null || !allowed.TryGetValue(key, out var expression)) return fallback;
        var dir = string.Equals(Query("dir"), "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
        return $"{expression} {dir}";
    }

    /// <summary>Query-string value, or null when absent or empty.</summary>
    protected string? Query(string name) => Request.Query[name].FirstOrDefault() is { Length: > 0 } v ? v : null;
}
