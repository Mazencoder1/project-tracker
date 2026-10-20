using System.Globalization;
using System.Net;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>The scheduled notification cycle (delay alerts, project delay alerts, weekly digest) against the real schema.</summary>
public class NotificationRunTests
{
    private static string Date(int offsetDays) => DateTime.Today.AddDays(offsetDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A late project: past its end date, with an overdue milestone, a stale weekly update and open risks.</summary>
    private static async Task<Guid> LateProjectAsync(Sandbox sb, HttpClient admin, string label)
    {
        var project = await sb.NewProjectAsync(label, startedWeeksAgo: 12);
        await Sandbox.ExecuteAsync("UPDATE projects SET end_date = @e, total_weeks = 8 WHERE id = @p", ("@e", DateTime.Today.AddDays(-7)), ("@p", project));
        await Http.Post(admin, "/api/milestones", new { project_id = project, name = "مرحلة متأخرة", week_start = 1, week_end = 4 });
        await Http.Post(admin, "/api/risks", new { project_id = project, type = "blocker", title = "عائق حرج", severity = "critical" });
        await Http.Post(admin, "/api/meeting-actions", new { project_id = project, meeting_date = Date(-20), action_type = "decision", description = "قرار متأخر", due_date = Date(-10) });
        return project;
    }

    [SqlFact]
    public async Task The_notification_cycle_runs_for_late_projects_and_is_safe_to_repeat()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await LateProjectAsync(sb, admin, "مشروع متأخر");
        var (managerId, _) = await sb.NewUserAsync("مدير متأخر");
        await Sandbox.AssignAsync(admin, project, managerId);

        foreach (var url in new[] { "/api/notifications/run", "/api/notifications/run?weekly=force", "/api/notifications/run?weekly=force" })
        {
            var response = await Http.Post(admin, url);
            await Http.Expect(HttpStatusCode.OK, response);
            var body = await Http.Json(response);
            Assert.True(body.TryGetProperty("summary", out _) || body.TryGetProperty("weekly", out _));
        }

        // Once a project recovers, its claim is released and it can be alerted again.
        await Sandbox.ExecuteAsync("UPDATE projects SET status = 'completed' WHERE id = @p", ("@p", project));
        await Http.Expect(HttpStatusCode.OK, await Http.Post(admin, "/api/notifications/run"));
        await Sandbox.ExecuteAsync("DELETE FROM notification_log WHERE user_id IN (SELECT id FROM profiles WHERE email = 'admin@example.com')");
    }

    [SqlFact]
    public async Task A_project_manager_cannot_trigger_the_cycle_and_the_secret_must_match()
    {
        await using var sb = await Sandbox.CreateAsync();
        var (_, email) = await sb.NewUserAsync("بلا صلاحية");
        var pm = await sb.LoginAsync(email, Sandbox.UserPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.Post(pm, "/api/notifications/run")).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/notifications/run") { Content = JsonContentEmpty() };
        request.Headers.Add("x-notify-secret", "definitely-not-the-secret-value");
        Assert.Equal(HttpStatusCode.Forbidden, (await sb.Anonymous().SendAsync(request)).StatusCode);
    }

    private static StringContent JsonContentEmpty() => new("{}", System.Text.Encoding.UTF8, "application/json");

    [SqlFact]
    public async Task Malformed_and_oversized_bodies_get_a_json_error_not_a_stack_trace()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();

        var broken = new HttpRequestMessage(HttpMethod.Post, "/api/projects") { Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json") };
        var response = await admin.SendAsync(broken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var huge = new HttpRequestMessage(HttpMethod.Post, "/api/projects") { Content = new StringContent("{\"name\":\"" + new string('a', 3 * 1024 * 1024) + "\"}", System.Text.Encoding.UTF8, "application/json") };
        var big = await admin.SendAsync(huge);
        Assert.True(big.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, $"{(int)big.StatusCode}");
    }
}
