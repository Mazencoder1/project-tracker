using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTracker.Web.Controllers;

/// <summary>
/// The page routes of the system (/login, /meeting, /pm, /admin, /settings ...). The pages themselves are the
/// original UI compiled into wwwroot, unchanged; this controller decides who may see which page and where the others go.
/// </summary>
[Route("")]
public sealed class PagesController(IWebHostEnvironment environment, SessionService sessions) : Controller
{
    private PhysicalFileResult Page(string file, int status = 200)
    {
        var path = Path.Combine(environment.WebRootPath, file);
        Response.StatusCode = status;
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(path, "text/html; charset=utf-8");
    }

    /// <summary>The login page. A signed-in user is sent to the home page of their role instead.</summary>
    [AllowAnonymous]
    [AcceptVerbs("GET", "HEAD")]
    [Route("login")]
    public async Task<IActionResult> Login()
    {
        var auth = await HttpContext.AuthenticateAsync(TrackerAuth.Scheme);
        if (auth.Succeeded) return Redirect(TrackerAuth.HomeFor(auth.Principal.SessionUser().Role));

        // Rejected or expired cookies are dropped so the browser stops sending them.
        if (HttpContext.Items[TrackerAuth.HadSessionCookies] is true) sessions.ClearCookies(Response);
        return Page("login.html");
    }

    [AcceptVerbs("GET", "HEAD")]
    [Route("")]
    public IActionResult Root() => Redirect(TrackerAuth.HomeFor(User.SessionUser().Role));

    [AcceptVerbs("GET", "HEAD")]
    [Route("meeting")]
    public IActionResult Meeting() => Page("meeting.html");

    /// <summary>Project managers' page. The admin goes to /admin and the deputy to /meeting.</summary>
    [AcceptVerbs("GET", "HEAD")]
    [Route("pm")]
    public IActionResult ProjectManager() => User.SessionUser().Role switch
    {
        "admin" => Redirect("/admin"),
        "deputy" => Redirect("/meeting"),
        _ => Page("pm.html"),
    };

    /// <summary>Administration. Project managers never see it; the deputy sees a reduced version (decided inside the page).</summary>
    [AcceptVerbs("GET", "HEAD")]
    [Route("admin")]
    public IActionResult Admin() => User.SessionUser().Role == "pm" ? Redirect("/pm") : Page("admin.html");

    [AcceptVerbs("GET", "HEAD")]
    [Route("settings")]
    public IActionResult Settings() => Page("settings.html");

    /// <summary>
    /// Settings → e-mail templates. Administrator only, decided here on the server (the API behind it is protected too):
    /// everyone else is sent to their own home page and the page file is never served to them.
    /// </summary>
    [AcceptVerbs("GET", "HEAD")]
    [Route("settings/email-templates")]
    public IActionResult EmailTemplates() => User.SessionUser().Role == "admin" ? Page("settings/email-templates.html") : Redirect(TrackerAuth.HomeFor(User.SessionUser().Role));

    /// <summary>Settings → system logs (e-mail, SMS, project creation, assignments). Administrator only, like the templates page.</summary>
    [AcceptVerbs("GET", "HEAD")]
    [Route("settings/logs")]
    public IActionResult SystemLogs() => User.SessionUser().Role == "admin" ? Page("settings/logs.html") : Redirect(TrackerAuth.HomeFor(User.SessionUser().Role));

    /// <summary>Any other page path answers with the system's own "not found" page.</summary>
    [AcceptVerbs("GET", "HEAD")]
    [Route("{**path}")]
    public IActionResult NotFoundPage(string? path) => Page("404.html", 404);
}
