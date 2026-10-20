using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>Small JSON helpers shared by the HTTP-level integration tests.</summary>
internal static class Http
{
    public static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> Post(HttpClient c, string url, object? body = null) => Send(c, HttpMethod.Post, url, body ?? new { });

    public static Task<HttpResponseMessage> Patch(HttpClient c, string url, object body) => Send(c, HttpMethod.Patch, url, body);

    public static Task<HttpResponseMessage> Put(HttpClient c, string url, object body) => Send(c, HttpMethod.Put, url, body);

    public static Task<HttpResponseMessage> Delete(HttpClient c, string url, object? body = null) => Send(c, HttpMethod.Delete, url, body);

    /// <summary>Asserts the status code and shows the response body when it differs (much easier to debug than the bare codes).</summary>
    public static async Task Expect(HttpStatusCode expected, HttpResponseMessage response)
    {
        var body = response.Content is null ? "" : await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"expected {(int)expected} but got {(int)response.StatusCode}: {body}");
    }

    public static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement;
    }

    public static async Task<JsonElement> GetJson(HttpClient c, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await c.GetAsync(url);
        Assert.Equal(expected, response.StatusCode);
        return await Json(response);
    }
}

/// <summary>Projects, milestones, tasks and notes through the real HTTP pipeline (skipped without TRACKER_TEST_DB).</summary>
public class ProjectApiTests
{
    private static object NewProjectBody(string name, string? department = null) => new
    {
        name = $"{Sandbox.Marker} {name}",
        description = "وصف تجريبي",
        start_date = DateTime.Today.AddDays(-14).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        end_date = DateTime.Today.AddDays(126).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        project_code = "T-" + Guid.NewGuid().ToString("N")[..6],
        department,
        priority = "high",
    };

    [SqlFact]
    public async Task Project_lifecycle_create_list_edit_and_delete()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();

        var created = await Http.Post(admin, "/api/projects", NewProjectBody("دورة المشروع"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await Http.Json(created)).GetProperty("id").GetString()!;
        sb.TrackProject(Guid.Parse(id));

        var list = await Http.GetJson(admin, $"/api/projects?q={Uri.EscapeDataString(Sandbox.Marker)}&sort=name&dir=desc&pageSize=5");
        Assert.True(list.GetProperty("projects").GetArrayLength() >= 1);
        Assert.True(list.GetProperty("stats").GetProperty("total").GetInt32() >= 1);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/projects?status=active&sort=status&page=2")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/projects/{id}", new { description = "وصف جديد", priority = "low" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/projects/{id}", new { name = $"{Sandbox.Marker} اسم مُعدَّل", status = "delayed" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/projects/{id}", new { status = "cancelled" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/projects/{id}", new { status = "active" })).StatusCode);

        // Rejections.
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/projects/{id}", new { status = "unknown" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/projects/{id}", new { name = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/projects/{id}", new { description = new string('س', 5000) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, "/api/projects/not-a-guid", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Patch(admin, $"/api/projects/{Guid.NewGuid()}", new { description = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/projects", new { name = "", start_date = "2026-01-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/projects", NewProjectBody("قسم غير معروف", "قسم غير موجود"))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Delete(admin, "/api/projects/xyz")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Delete(admin, $"/api/projects/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Delete(admin, $"/api/projects/{id}")).StatusCode);
    }

    [SqlFact]
    public async Task Schedule_edits_are_checked_against_existing_milestones()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("جدول زمني");
        await Http.Post(admin, "/api/milestones", new { project_id = project, name = "مرحلة طويلة", week_start = 1, week_end = 18 });

        var start = DateTime.Today.AddDays(-14);
        var shorten = await Http.Patch(admin, $"/api/projects/{project}", new { start_date = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end_date = start.AddDays(35).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        Assert.Equal(HttpStatusCode.BadRequest, shorten.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/projects/{project}", new { end_date = "2000-01-01" })).StatusCode);
        var extend = await Http.Patch(admin, $"/api/projects/{project}", new { end_date = start.AddDays(200).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        await Http.Expect(HttpStatusCode.OK, extend);
    }

    [SqlFact]
    public async Task Milestones_tasks_and_notes_work_end_to_end_with_validation()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("هيكل العمل");

        Assert.Equal(HttpStatusCode.OK, (await Http.Post(admin, "/api/milestones", new { project_id = project, name = "المرحلة الأولى", week_start = 1, week_end = 8, color = "#1b5e9f" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/milestones", new { project_id = project, name = "", week_start = 1, week_end = 8 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/milestones", new { project_id = project, name = "خارج المدة", week_start = 1, week_end = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/milestones", new { project_id = "bad", name = "x", week_start = 1, week_end = 2 })).StatusCode);

        var milestones = await Http.GetJson(admin, $"/api/milestones?project_id={project}");
        var milestone = milestones.GetProperty("milestones")[0].GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/milestones")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/milestones/{milestone}", new { name = "المرحلة الأولى مُعدَّلة", week_start = 1, week_end = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/milestones/{milestone}", new { name = "", week_start = 1, week_end = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, "/api/milestones/zzz", new { name = "x" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Http.Post(admin, "/api/tasks", new { milestone_id = milestone, name = "مهمة فرعية", week_start = 1, week_end = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/tasks", new { milestone_id = milestone, name = "خارج النطاق", week_start = 1, week_end = 40 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/tasks", new { milestone_id = "nope", name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Post(admin, "/api/tasks", new { milestone_id = Guid.NewGuid(), name = "x", week_start = 1, week_end = 2 })).StatusCode);

        var tasks = await Http.GetJson(admin, $"/api/tasks?project_id={project}");
        var task = tasks.GetProperty("tasks")[0].GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/tasks?project_id={project}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/tasks/{task}", new { progress = 40 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/tasks/{task}", new { name = "مهمة مُعدَّلة", week_start = 1, week_end = 4, progress = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/tasks/{task}", new { progress = 250 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, "/api/tasks/bad", new { progress = 1 })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Http.Post(admin, "/api/notes", new { milestone_id = milestone, type = "detail", content = "تفصيل" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Post(admin, "/api/notes", new { milestone_id = milestone, type = "support", content = "نحتاج دعمًا" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/notes", new { milestone_id = milestone, type = "other", content = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/notes", new { milestone_id = milestone, type = "detail", content = "" })).StatusCode);
        var notes = await Http.GetJson(admin, $"/api/notes?project_id={project}");
        var note = notes.GetProperty("notes")[0].GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await Http.Delete(admin, $"/api/notes/{note}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Delete(admin, $"/api/notes/{Guid.NewGuid()}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Http.Delete(admin, $"/api/tasks/{task}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Delete(admin, $"/api/tasks/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Delete(admin, $"/api/milestones/{milestone}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Delete(admin, $"/api/milestones/{Guid.NewGuid()}")).StatusCode);
    }

    [SqlFact]
    public async Task A_project_manager_only_writes_to_assigned_projects()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var mine = await sb.NewProjectAsync("مشروعي");
        var other = await sb.NewProjectAsync("مشروع غيري");
        var (userId, email) = await sb.NewUserAsync("مدير مشروع");
        await Sandbox.AssignAsync(admin, mine, userId);
        sb.Smtp.Clear();
        var pm = await sb.LoginAsync(email, Sandbox.UserPassword);

        Assert.Equal(HttpStatusCode.OK, (await Http.Post(pm, "/api/milestones", new { project_id = mine, name = "مرحلتي", week_start = 1, week_end = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.Post(pm, "/api/milestones", new { project_id = other, name = "ليست لي", week_start = 1, week_end = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.Post(pm, "/api/projects", NewProjectBody("ممنوع"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.Patch(pm, $"/api/projects/{mine}", new { description = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Http.Delete(pm, $"/api/projects/{mine}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Sandbox.AssignAsync(pm, mine, userId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pm.GetAsync("/api/users")).StatusCode);

        var visible = await Http.GetJson(pm, "/api/projects?pageSize=100");
        var ids = visible.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("id").GetString()!.ToLowerInvariant()).ToList();
        Assert.Contains(mine.ToString(), ids);
        Assert.DoesNotContain(other.ToString(), ids);

        var assignments = await Http.GetJson(admin, $"/api/assignments?project_id={mine}");
        Assert.True(assignments.GetProperty("assignments").GetArrayLength() >= 1);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/assignments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Sandbox.AssignAsync(admin, Guid.NewGuid(), userId)).StatusCode);
    }

    [SqlFact]
    public async Task Anonymous_calls_and_unknown_paths_are_rejected_as_json()
    {
        await using var sb = await Sandbox.CreateAsync();
        var anonymous = sb.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me-does-not-exist")).StatusCode);

        var admin = await sb.AdminAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/not-a-real-endpoint")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/departments")).StatusCode);
    }
}
