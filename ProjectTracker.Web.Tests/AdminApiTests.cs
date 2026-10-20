using System.Globalization;
using System.Net;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>Users, sessions, password flows, the executive endpoints and settings (skipped without TRACKER_TEST_DB).</summary>
public class AdminApiTests
{
    private static string Today(int offsetDays = 0) => DateTime.Today.AddDays(offsetDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static object NewUser(string number, string email, string role = "pm") => new
    {
        employee_number = number,
        full_name = $"{Sandbox.Marker} موظف {number}",
        job_title = "مهندس",
        email,
        department = "تقنية المعلومات",
        mobile_no = "0500000000",
        role,
        password_hash = Sandbox.UserPassword,
    };

    private static async Task<Guid> TrackedUserIdAsync(Sandbox sb, string email)
    {
        var rows = await Sandbox.QueryAsync("SELECT id FROM profiles WHERE email = @e", ("@e", email));
        var id = (Guid)rows[0][0]!;
        sb.TrackUser(id);
        return id;
    }

    [SqlFact]
    public async Task User_lifecycle_create_list_edit_protect_and_delete()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var suffix = Random.Shared.Next(100000, 999999);
        var email = $"u{suffix}@test.example";
        var number = $"9{suffix}";

        await Http.Expect(HttpStatusCode.Created, await Http.Post(admin, "/api/users", NewUser(number, email)));
        var id = await TrackedUserIdAsync(sb, email);

        Assert.Equal(HttpStatusCode.Conflict, (await Http.Post(admin, "/api/users", NewUser(number, $"x{email}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Http.Post(admin, "/api/users", NewUser($"8{suffix}", email))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/users", NewUser("", email))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/users", NewUser("12ab", email))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/users", NewUser($"7{suffix}", "not-an-email"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/users", NewUser($"6{suffix}", $"y{email}", "boss"))).StatusCode);

        var list = await Http.GetJson(admin, $"/api/users?q={Uri.EscapeDataString(Sandbox.Marker)}&role=pm&status=active&sort=name&dir=desc");
        Assert.True(list.GetProperty("users").GetArrayLength() >= 1);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users?sort=email&status=disabled&page=3")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/users/{id}", new { full_name = $"{Sandbox.Marker} اسم جديد", department = "الموارد البشرية" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/users/{id}", new { full_name = $"{Sandbox.Marker} اسم جديد" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/users/{id}", new { role = "deputy" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/users/{id}", new { status = "disabled" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/users/{id}", new { status = "active", password = "Brand#New2026!x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/users/{id}", new { status = "deleted" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/users/{id}", new { status = "weird" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/users/{id}", new { password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, "/api/users/bad-id", new { role = "pm" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Patch(admin, $"/api/users/{Guid.NewGuid()}", new { role = "pm" })).StatusCode);

        // The administrator cannot suspend or demote themselves.
        var me = await Http.GetJson(admin, "/api/users?q=admin%40example.com");
        var myId = me.GetProperty("users").EnumerateArray().First(u => u.GetProperty("email").GetString() == Sandbox.AdminEmail).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/users/{myId}", new { status = "disabled" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/users/{myId}", new { role = "pm" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Delete(admin, $"/api/users/{myId}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Http.Delete(admin, $"/api/users/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Delete(admin, "/api/users/bad")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Delete(admin, $"/api/users/{Guid.NewGuid()}")).StatusCode);
    }

    [SqlFact]
    public async Task Login_session_logout_and_password_change()
    {
        await using var sb = await Sandbox.CreateAsync();
        var (_, email) = await sb.NewUserAsync("دخول");
        var anonymous = sb.Anonymous();

        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(anonymous, "/api/auth/login", new { email = "", password = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.Post(anonymous, "/api/auth/login", new { email, password = "wrong-password" })).StatusCode);

        var pm = await sb.LoginAsync(email, Sandbox.UserPassword);
        Assert.Equal(HttpStatusCode.OK, (await pm.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(pm, "/api/auth/change-password", new { currentPassword = Sandbox.UserPassword, newPassword = "short", confirmPassword = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(pm, "/api/auth/change-password", new { currentPassword = Sandbox.UserPassword, newPassword = "Another#Pass2026", confirmPassword = "Different#Pass2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(pm, "/api/auth/change-password", new { currentPassword = "not-my-password", newPassword = "Another#Pass2026", confirmPassword = "Another#Pass2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Post(pm, "/api/auth/change-password", new { currentPassword = Sandbox.UserPassword, newPassword = "Another#Pass2026", confirmPassword = "Another#Pass2026" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Http.Post(pm, "/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await pm.GetAsync("/api/projects")).StatusCode);
        var again = await sb.LoginAsync(email, "Another#Pass2026");
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/api/projects")).StatusCode);
    }

    [SqlFact]
    public async Task Password_recovery_flows_answer_every_case()
    {
        await using var sb = await Sandbox.CreateAsync();
        var (_, email) = await sb.NewUserAsync("نسيان");
        var anonymous = sb.Anonymous();

        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(anonymous, "/api/auth/forgot-password", new { email = "nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Post(anonymous, "/api/auth/forgot-password", new { email = "nobody@test.example" })).StatusCode);
        var sent = await Http.Post(anonymous, "/api/auth/forgot-password", new { email });
        Assert.True(sent.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);
        var again = await Http.Post(anonymous, "/api/auth/forgot-password", new { email });
        Assert.True(again.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable);

        Assert.Equal(HttpStatusCode.Gone, (await Http.Post(anonymous, "/api/auth/forgot-password/confirm", new { token = "garbage" })).StatusCode);

        var pm = await sb.LoginAsync(email, Sandbox.UserPassword);
        var reset = await Http.Post(pm, "/api/auth/reset-password");
        Assert.True(reset.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests);
    }

    [SqlFact]
    public async Task Executive_endpoints_risks_actions_and_weekly_updates()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("لوحة تنفيذية");
        await Http.Post(admin, "/api/milestones", new { project_id = project, name = "مرحلة", week_start = 1, week_end = 10 });
        var milestone = (await Http.GetJson(admin, $"/api/milestones?project_id={project}")).GetProperty("milestones")[0].GetProperty("id").GetString()!;
        var (ownerId, _) = await sb.NewUserAsync("مسؤول");

        // Risks.
        await Http.Expect(HttpStatusCode.Created, await Http.Post(admin, "/api/risks", new { project_id = project, type = "risk", title = "خطر تجريبي", severity = "critical", probability = "high", status = "open", owner_id = ownerId, milestone_id = milestone, due_date = Today(-3), mitigation = "خطة" }));
        await Http.Expect(HttpStatusCode.Created, await Http.Post(admin, "/api/risks", new { project_id = project, type = "blocker", title = "عائق تجريبي", severity = "high" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/risks", new { project_id = project, type = "nope", title = "x", severity = "low" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/risks", new { project_id = project, type = "risk", title = "ab", severity = "low" })).StatusCode);
        var risks = await Http.GetJson(admin, $"/api/risks?project_id={project}&status=active");
        var riskId = risks.GetProperty("risks")[0].GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/risks?project_id={project}&status=closed")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/risks?status=bogus")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/risks?project_id=bad")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/risks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/risks/{riskId}", new { status = "mitigating", mitigation = "تحديث الخطة" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/risks/{riskId}", new { status = "closed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/risks/{riskId}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/risks/{riskId}", new { severity = "huge" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Patch(admin, $"/api/risks/{Guid.NewGuid()}", new { status = "open" })).StatusCode);

        // Meeting actions.
        await Http.Expect(HttpStatusCode.Created, await Http.Post(admin, "/api/meeting-actions", new { project_id = project, meeting_date = Today(-2), action_type = "support", description = "نحتاج دعمًا من الإدارة", status = "new", owner_id = ownerId, due_date = Today(-1), supporting_department = "المالية" }));
        await Http.Expect(HttpStatusCode.Created, await Http.Post(admin, "/api/meeting-actions", new { project_id = project, meeting_date = Today(-2), action_type = "decision", description = "قرار مطلوب سريعًا", due_date = Today(-1) }));
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/meeting-actions", new { project_id = project, action_type = "support", description = "x" })).StatusCode);
        var actions = await Http.GetJson(admin, $"/api/meeting-actions?project_id={project}&status=open&action_type=support");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/meeting-actions")).StatusCode);
        var actionId = (await Http.GetJson(admin, $"/api/meeting-actions?project_id={project}")).GetProperty("actions")[0].GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/meeting-actions/{actionId}", new { status = "in_progress" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/meeting-actions/{actionId}", new { status = "completed" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, $"/api/meeting-actions/{actionId}", new { status = "completed", closure_notes = "أُنجز" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, $"/api/meeting-actions/{actionId}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Http.Patch(admin, $"/api/meeting-actions/{Guid.NewGuid()}", new { status = "new" })).StatusCode);
        _ = actions;

        // Weekly updates.
        await Http.Expect(HttpStatusCode.Created, await Http.Post(admin, "/api/project-updates", new
        {
            project_id = project, reporting_date = Today(), executive_summary = "ملخص", actual_progress = 30, planned_progress = 50, health_status = "delayed",
            delay_reason = "نقص موارد", current_milestone_id = milestone, support_required = "دعم مالي", support_from = "المالية", decision_required = "اعتماد ميزانية",
            decision_from = "المدير", decision_needed_by = Today(-1), decision_status = "new",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Post(admin, "/api/project-updates", new { project_id = project, reporting_date = Today(5), health_status = "delayed" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/project-updates?project_id={project}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/project-updates")).StatusCode);

        // Dashboards.
        var summary = await Http.GetJson(admin, $"/api/portfolio/{project}");
        Assert.True(summary.TryGetProperty("project", out _) || summary.ValueKind == System.Text.Json.JsonValueKind.Object);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/portfolio/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/portfolio/bad")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/portfolio")).StatusCode);
        var executive = await Http.GetJson(admin, "/api/dashboard/executive");
        Assert.True(executive.TryGetProperty("kpis", out _));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/people")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit-log?entity_type=risk&page=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit-log")).StatusCode);
    }

    [SqlFact]
    public async Task Dashboard_settings_and_the_notification_trigger()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();

        var current = await Http.GetJson(admin, "/api/app-settings");
        var days = current.GetProperty("settings").GetProperty("staleUpdateDays").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Http.Patch(admin, "/api/app-settings", new { staleUpdateDays = days })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, "/api/app-settings", new { staleUpdateDays = 9999 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Patch(admin, "/api/app-settings", new { varianceOnTrack = "x" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Http.Post(sb.Anonymous(), "/api/notifications/run")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Post(admin, "/api/notifications/run")).StatusCode);
    }

    [SqlFact]
    public async Task Email_template_endpoints_and_page_routes()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var (_, pmEmail) = await sb.NewUserAsync("قوالب");
        var pm = await sb.LoginAsync(pmEmail, Sandbox.UserPassword);

        var list = await Http.GetJson(admin, "/api/email-templates");
        var key = list.GetProperty("templates")[0].GetProperty("templateKey").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/email-templates/{key}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/email-templates/NoSuchTemplate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Post(admin, $"/api/email-templates/{key}/preview", new { subject = "موضوع {{ProjectName}}", messageText = "مرحبًا {{ProjectName}}" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Put(admin, $"/api/email-templates/{key}", new { subject = "{{Unknown}}", messageText = "نص", isEnabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Put(admin, $"/api/email-templates/{key}", new { subject = "", messageText = "", isEnabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Post(admin, $"/api/email-templates/{key}/test", new { to = "someone@test.example" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Http.Put(admin, "/api/email-settings/weekly-reminder", new { enabled = true, day = 0, time = "08:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.Put(admin, "/api/email-settings/weekly-reminder", new { enabled = true, day = 9, time = "25:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pm.GetAsync("/api/email-templates")).StatusCode);

        // Page routes: the compiled UI is served according to the signed-in role.
        Assert.Equal(HttpStatusCode.OK, (await sb.Anonymous().GetAsync("/login")).StatusCode);
        foreach (var path in new[] { "/", "/meeting", "/pm", "/admin", "/settings", "/settings/email-templates", "/settings/logs", "/no-such-page" })
        {
            var response = await admin.GetAsync(path);
            Assert.True((int)response.StatusCode < 500, $"{path} -> {(int)response.StatusCode}");
            var asPm = await pm.GetAsync(path);
            Assert.True((int)asPm.StatusCode < 500, $"{path} as pm -> {(int)asPm.StatusCode}");
            var anon = await sb.Anonymous().GetAsync(path);
            Assert.True((int)anon.StatusCode < 500, $"{path} anonymous -> {(int)anon.StatusCode}");
        }
    }
}
