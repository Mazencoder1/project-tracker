using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProjectTracker.Web.Services.Email;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>
/// The scenarios of the e-mail feature, run against the real application: HTTP → controllers → notifier → template
/// service → mail client → a fake SMTP relay, with the real SQL Server schema. (Skipped without TRACKER_TEST_DB.)
/// </summary>
public class EmailNotificationScenarioTests
{
    private static string Sunday0805Riyadh => "2026-09-27T05:05:00Z"; // Sunday 08:05 in Asia/Riyadh (UTC+3)

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // ── ProjectAssigned ─────────────────────────────────────────────────────
    [SqlFact]
    public async Task Assigning_a_project_sends_exactly_one_email_built_from_the_database_template()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع الإسناد");
        var (userId, address) = await sb.NewUserAsync("مستخدم");

        var response = await Sandbox.AssignAsync(admin, project, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("notified").GetBoolean());
        var mail = Assert.Single(sb.Smtp.Mails);
        Assert.Equal(address, mail.To.Single(), ignoreCase: true);
        Assert.Equal($"تم إسناد مشروع جديد لك - {Sandbox.Marker} مشروع الإسناد", mail.Subject);
        Assert.Contains($"{Sandbox.Marker} مشروع الإسناد", mail.HtmlBody);
        Assert.Contains($"{Sandbox.BaseUrl}/meeting?project={project}", mail.HtmlBody);
        Assert.DoesNotContain("localhost", mail.HtmlBody);
        Assert.DoesNotContain("{{", mail.HtmlBody);
        var log = Assert.Single(await Sandbox.LogAsync(project, EmailTemplateKeys.ProjectAssigned));
        Assert.Equal("Sent", log.Status);
    }

    [SqlFact]
    public async Task Assigning_again_or_refreshing_does_not_send_a_second_email()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع التكرار");
        var (userId, _) = await sb.NewUserAsync("مستخدم");

        await Sandbox.AssignAsync(admin, project, userId);
        var refresh = await Sandbox.AssignAsync(admin, project, userId);          // browser refresh / double POST
        var refresh2 = await Sandbox.AssignAsync(admin, project, userId);

        Assert.True((await JsonAsync(refresh)).GetProperty("alreadyAssigned").GetBoolean());
        Assert.True((await JsonAsync(refresh2)).GetProperty("alreadyAssigned").GetBoolean());
        Assert.Single(sb.Smtp.Mails);
        Assert.Single(await Sandbox.LogAsync(project));
    }

    [SqlFact]
    public async Task Concurrent_double_submit_sends_one_email()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع التزامن");
        var (userId, _) = await sb.NewUserAsync("مستخدم");

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Sandbox.AssignAsync(admin, project, userId)));

        Assert.Single(sb.Smtp.Mails);
        Assert.Equal(1, await Sandbox.ScalarAsync("SELECT COUNT(*) FROM assignments WHERE project_id=@p", ("@p", project)));
    }

    [SqlFact]
    public async Task The_same_assignment_can_never_be_notified_twice_even_if_the_notifier_is_called_again()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع");
        var (userId, _) = await sb.NewUserAsync("مستخدم");
        await Sandbox.AssignAsync(admin, project, userId);
        var assignmentId = (Guid)(await Sandbox.QueryAsync("SELECT id FROM assignments WHERE project_id=@p", ("@p", project)))[0][0]!;

        var outcome = await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().NotifyProjectAssignedAsync(assignmentId, project.ToString(), userId.ToString()));

        Assert.Equal(EmailNotifyOutcome.AlreadySent, outcome);
        Assert.Single(sb.Smtp.Mails);
    }

    [SqlFact]
    public async Task Renaming_or_editing_a_project_does_not_send_ProjectAssigned()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("الاسم القديم");
        var (userId, _) = await sb.NewUserAsync("مستخدم");
        await Sandbox.AssignAsync(admin, project, userId);
        sb.Smtp.Clear();

        var rename = await Sandbox.PatchProjectAsync(admin, project, new { name = $"{Sandbox.Marker} الاسم الجديد" });
        var describe = await Sandbox.PatchProjectAsync(admin, project, new { description = "وصف جديد" });

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(HttpStatusCode.OK, describe.StatusCode);
        Assert.Empty(sb.Smtp.Mails);
        Assert.Single(await Sandbox.LogAsync(project));
    }

    [SqlFact]
    public async Task Changing_the_responsible_user_emails_the_new_user_once_and_a_new_assignment_is_a_new_email()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع تغيير المسؤول");
        var (first, firstAddress) = await sb.NewUserAsync("المسؤول القديم");
        var (second, secondAddress) = await sb.NewUserAsync("المسؤول الجديد");

        await Sandbox.AssignAsync(admin, project, first);
        Assert.Equal(HttpStatusCode.OK, (await Sandbox.UnassignAsync(admin, project, first)).StatusCode);
        await Sandbox.AssignAsync(admin, project, second);

        Assert.Single(sb.Smtp.To(firstAddress));
        Assert.Single(sb.Smtp.To(secondAddress));

        // Assigned again later = a new assignment = a new message (the key carries the assignment's own id).
        await Sandbox.AssignAsync(admin, project, first);
        Assert.Equal(2, sb.Smtp.To(firstAddress).Count());
        Assert.Single(sb.Smtp.To(secondAddress));
    }

    [SqlFact]
    public async Task A_disabled_template_sends_nothing_and_does_not_block_the_assignment()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/email-templates/ProjectAssigned", new { isEnabled = false })).StatusCode);
        var project = await sb.NewProjectAsync("مشروع");
        var (userId, _) = await sb.NewUserAsync("مستخدم");

        var response = await Sandbox.AssignAsync(admin, project, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await JsonAsync(response)).GetProperty("notified").GetBoolean());
        Assert.Empty(sb.Smtp.Mails);
        Assert.Equal(1, await Sandbox.ScalarAsync("SELECT COUNT(*) FROM assignments WHERE project_id=@p", ("@p", project)));
        Assert.Empty(await Sandbox.LogAsync(project));
    }

    [SqlFact]
    public async Task When_SMTP_fails_the_assignment_is_saved_and_the_email_is_logged_as_Failed_without_secrets()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع");
        var (userId, address) = await sb.NewUserAsync("مستخدم");
        sb.Smtp.Reject = true;

        var response = await Sandbox.AssignAsync(admin, project, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(body.GetProperty("notified").GetBoolean());
        Assert.Equal(1, await Sandbox.ScalarAsync("SELECT COUNT(*) FROM assignments WHERE project_id=@p AND user_id=@u", ("@p", project), ("@u", userId)));
        var log = Assert.Single(await Sandbox.LogAsync(project));
        Assert.Equal("Failed", log.Status);
        Assert.Equal(address, log.Recipient, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(log.Error));
        Assert.Empty(sb.Smtp.Mails);
    }

    [SqlFact]
    public async Task A_user_without_an_email_does_not_break_the_assignment()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع");
        var (noEmail, _) = await sb.NewUserAsync("بلا بريد", email: null);
        var (badEmail, _) = await sb.NewUserAsync("بريد سيئ", email: "not-an-email");

        var first = await Sandbox.AssignAsync(admin, project, noEmail);
        var second = await Sandbox.AssignAsync(admin, project, badEmail);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Empty(sb.Smtp.Mails);
        var log = await Sandbox.LogAsync(project);
        Assert.Equal(2, log.Count);
        Assert.All(log, row => Assert.Equal("Failed", row.Status));
        Assert.Contains(log, row => row.Error!.Contains("no e-mail"));
        Assert.Contains(log, row => row.Error!.Contains("not valid"));
    }

    // ── ProjectCompleted ────────────────────────────────────────────────────
    [SqlFact]
    public async Task Completing_a_project_sends_one_email_per_manager_and_editing_a_completed_project_never_repeats_it()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع الإكمال");
        var (a, addressA) = await sb.NewUserAsync("مسؤول 1");
        var (b, addressB) = await sb.NewUserAsync("مسؤول 2");
        await Sandbox.AssignAsync(admin, project, a);
        await Sandbox.AssignAsync(admin, project, b);
        sb.Smtp.Clear();

        Assert.Equal(HttpStatusCode.OK, (await Sandbox.PatchProjectAsync(admin, project, new { status = "completed" })).StatusCode);

        var mailA = Assert.Single(sb.Smtp.To(addressA));
        Assert.Single(sb.Smtp.To(addressB));
        Assert.Equal($"تم إكمال المشروع - {Sandbox.Marker} مشروع الإكمال", mailA.Subject);
        Assert.Contains($"{Sandbox.BaseUrl}/meeting?project={project}", mailA.HtmlBody);
        Assert.Contains(sb.Clock.Now.AddHours(3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), mailA.HtmlBody);   // completion date = today in the system time zone (from the injected clock)

        // Editing the completed project, and saving "completed" again, are not transitions.
        await Sandbox.PatchProjectAsync(admin, project, new { description = "تعديل لاحق" });
        await Sandbox.PatchProjectAsync(admin, project, new { name = $"{Sandbox.Marker} اسم آخر" });
        await Sandbox.PatchProjectAsync(admin, project, new { status = "completed" });
        Assert.Equal(2, sb.Smtp.Mails.Count);
        Assert.Equal(2, (await Sandbox.LogAsync(project, EmailTemplateKeys.ProjectCompleted)).Count);
    }

    [SqlFact]
    public async Task Reopening_and_completing_again_does_not_repeat_the_completion_email()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع");
        var (a, _) = await sb.NewUserAsync("مسؤول");
        await Sandbox.AssignAsync(admin, project, a);
        await Sandbox.PatchProjectAsync(admin, project, new { status = "completed" });
        sb.Smtp.Clear();

        await Sandbox.PatchProjectAsync(admin, project, new { status = "active" });
        await Sandbox.PatchProjectAsync(admin, project, new { status = "completed" });

        Assert.Empty(sb.Smtp.Mails);   // ProjectCompleted:{ProjectId}:{UserId} is the once-only key the requirements define
    }

    [SqlFact]
    public async Task Other_status_changes_do_not_send_the_completion_email()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع");
        var (a, _) = await sb.NewUserAsync("مسؤول");
        await Sandbox.AssignAsync(admin, project, a);
        sb.Smtp.Clear();

        await Sandbox.PatchProjectAsync(admin, project, new { status = "delayed" });
        await Sandbox.PatchProjectAsync(admin, project, new { status = "cancelled" });

        Assert.Empty(sb.Smtp.Mails);
    }

    // ── ProjectWeeklyReminder ───────────────────────────────────────────────
    [SqlFact]
    public async Task Weekly_reminder_goes_once_per_project_and_user_per_week_and_never_for_completed_projects()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var active = await sb.NewProjectAsync("مشروع نشط");
        var done = await sb.NewProjectAsync("مشروع منتهي", status: "completed");
        var (a, addressA) = await sb.NewUserAsync("مسؤول 1");
        var (b, addressB) = await sb.NewUserAsync("مسؤول 2");
        await Sandbox.AssignAsync(admin, active, a);
        await Sandbox.AssignAsync(admin, active, b);
        await Sandbox.AssignAsync(admin, done, a);
        sb.Smtp.Clear();
        sb.Clock.Now = DateTimeOffset.Parse(Sunday0805Riyadh);

        var first = await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        Assert.True(first.Ran);
        Assert.Equal(2, first.Sent);
        Assert.Equal(2, sb.Smtp.Mails.Count);
        Assert.Single(sb.Smtp.To(addressA));
        Assert.Single(sb.Smtp.To(addressB));
        Assert.All(sb.Smtp.Mails, m => Assert.Contains("مشروع نشط", m.Subject));

        // Same window again (the job runs every few minutes) and later the same day: nothing.
        await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        sb.Clock.Now = sb.Clock.Now.AddHours(6);
        var later = await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        Assert.Equal(0, later.Sent);
        Assert.Equal(2, sb.Smtp.Mails.Count);
        Assert.Empty(await Sandbox.LogAsync(done, EmailTemplateKeys.ProjectWeeklyReminder));

        // The next week's window sends again, once.
        sb.Clock.Now = DateTimeOffset.Parse(Sunday0805Riyadh).AddDays(7);
        await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        Assert.Equal(4, sb.Smtp.Mails.Count);
    }

    [SqlFact]
    public async Task Weekly_reminder_only_runs_inside_the_configured_window()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع");
        var (a, _) = await sb.NewUserAsync("مسؤول");
        await Sandbox.AssignAsync(admin, project, a);
        sb.Smtp.Clear();

        foreach (var notDue in new[] { "2026-09-27T04:59:00Z", "2026-09-28T06:00:00Z", "2026-09-30T05:05:00Z" })   // 07:59 Sunday, 25h later, midweek
        {
            sb.Clock.Now = DateTimeOffset.Parse(notDue);
            var summary = await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
            Assert.False(summary.Ran);
        }
        Assert.Empty(sb.Smtp.Mails);
    }

    [SqlFact]
    public async Task A_server_restart_does_not_resend_the_weekly_reminder()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع");
        var (a, _) = await sb.NewUserAsync("مسؤول");
        await Sandbox.AssignAsync(admin, project, a);
        sb.Smtp.Clear();
        sb.Clock.Now = DateTimeOffset.Parse(Sunday0805Riyadh);
        await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        Assert.Single(sb.Smtp.Mails);

        sb.RestartServer();                                                       // a brand-new host, empty memory
        var again = await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());

        Assert.Equal(0, again.Sent);
        Assert.Single(sb.Smtp.Mails);
    }

    [SqlFact]
    public async Task Two_servers_running_the_job_at_the_same_moment_send_each_reminder_once()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var users = new List<Guid>();
        var project = await sb.NewProjectAsync("مشروع");
        for (var i = 0; i < 4; i++) { var (id, _) = await sb.NewUserAsync($"مسؤول {i}"); users.Add(id); await Sandbox.AssignAsync(admin, project, id); }
        sb.Smtp.Clear();
        sb.Clock.Now = DateTimeOffset.Parse(Sunday0805Riyadh);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync())));

        Assert.Equal(4, sb.Smtp.Mails.Count);
        Assert.Equal(4, (await Sandbox.LogAsync(project, EmailTemplateKeys.ProjectWeeklyReminder)).Count(r => r.Status == "Sent"));
    }

    [SqlFact]
    public async Task Weekly_reminder_carries_status_progress_dates_days_remaining_overdue_tasks_and_link()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع التذكير", startedWeeksAgo: 8);
        await Sandbox.AddOverdueTaskAsync(project, "مهمة متأخرة للاختبار");
        var (a, _) = await sb.NewUserAsync("مسؤول");
        await Sandbox.AssignAsync(admin, project, a);
        sb.Smtp.Clear();
        sb.Clock.Now = DateTimeOffset.Parse(Sunday0805Riyadh);

        await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());

        var mail = Assert.Single(sb.Smtp.Mails);
        Assert.Contains("مهمة متأخرة للاختبار", mail.HtmlBody);
        Assert.Contains("نشط", mail.HtmlBody);                          // ProjectStatus
        Assert.Contains($"{Sandbox.BaseUrl}/meeting?project={project}", mail.HtmlBody);
        Assert.Contains("أيام", mail.HtmlBody);                          // DaysRemaining
        Assert.DoesNotContain("{{", mail.HtmlBody);
    }

    [SqlFact]
    public async Task A_dead_mail_relay_stops_the_weekly_run_early_and_the_reminders_are_sent_once_it_recovers()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع");
        for (var i = 0; i < 5; i++) { var (id, _) = await sb.NewUserAsync($"مسؤول {i}"); await Sandbox.AssignAsync(admin, project, id); }
        sb.Smtp.Clear();
        sb.Clock.Now = DateTimeOffset.Parse(Sunday0805Riyadh);
        sb.Smtp.Reject = true;

        var down = await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());

        Assert.Equal(3, down.Failed);                                    // gave up after 3 failures in a row, did not grind through all 5
        Assert.Empty(sb.Smtp.Mails);
        var afterFailure = await Sandbox.LogAsync(project, EmailTemplateKeys.ProjectWeeklyReminder);
        Assert.Equal(3, afterFailure.Count(r => r.Status == "Failed"));
        Assert.All(afterFailure, r => Assert.False(string.IsNullOrWhiteSpace(r.Error)));

        // Right after, nothing is retried (30-minute pause); once the relay is back and the pause has passed, everyone gets exactly one.
        sb.Smtp.Reject = false;
        var tooSoon = await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        Assert.Equal(2, tooSoon.Sent);                                    // the 2 never attempted; the 3 failed ones wait
        sb.Clock.Now = sb.Clock.Now.AddMinutes(31);
        // The pause is measured by the database clock, so backdate the failed rows instead of sleeping in the test.
        await Sandbox.ExecuteAsync("UPDATE EmailNotificationLog SET UpdatedAt = DATEADD(MINUTE, -31, SYSUTCDATETIME()) WHERE ProjectId = @p AND Status = 'Failed'", ("@p", project));
        var recovered = await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        Assert.Equal(3, recovered.Sent);
        await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        Assert.Equal(5, sb.Smtp.Mails.Count);
        Assert.Equal(5, (await Sandbox.LogAsync(project, EmailTemplateKeys.ProjectWeeklyReminder)).Count(r => r.Status == "Sent"));
    }

    [SqlFact]
    public async Task Weekly_reminder_switch_day_and_time_come_from_the_system_settings()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع");
        var (a, _) = await sb.NewUserAsync("مسؤول");
        await Sandbox.AssignAsync(admin, project, a);
        sb.Smtp.Clear();

        // Wednesday 14:30 Riyadh = 11:30 UTC
        var save = await admin.PutAsJsonAsync("/api/email-settings/weekly-reminder", new { enabled = true, day = 3, time = "14:30" });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        sb.Clock.Now = DateTimeOffset.Parse("2026-09-30T11:30:00Z");
        await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        Assert.Single(sb.Smtp.Mails);

        // Disabled: the next week's window passes without a message.
        await admin.PutAsJsonAsync("/api/email-settings/weekly-reminder", new { enabled = false });
        sb.Clock.Now = DateTimeOffset.Parse("2026-10-07T11:30:00Z");
        var summary = await sb.InScopeAsync(s => s.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync());
        Assert.Equal("disabled", summary.SkippedReason);
        Assert.Single(sb.Smtp.Mails);
    }

    [SqlFact]
    public async Task Weekly_reminder_settings_reject_bad_values()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/email-settings/weekly-reminder", new { day = 9 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/email-settings/weekly-reminder", new { time = "25:99" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/email-settings/weekly-reminder", new { time = "8:00" })).StatusCode);
    }

    // ── administration: permissions ─────────────────────────────────────────
    [SqlFact]
    public async Task Only_an_administrator_can_reach_the_template_settings_pages_and_api()
    {
        await using var sb = await Sandbox.CreateAsync();
        var (_, pmAddress) = await sb.NewUserAsync("مدير مشروع", role: "pm");
        var (_, deputyAddress) = await sb.NewUserAsync("وكيل", role: "deputy");
        var pm = await sb.LoginAsync(pmAddress, Sandbox.UserPassword);
        var deputy = await sb.LoginAsync(deputyAddress, Sandbox.UserPassword);
        var admin = await sb.AdminAsync();
        var anonymous = sb.Anonymous();

        // Anonymous: no session.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/email-templates")).StatusCode);

        foreach (var (who, client) in new[] { ("pm", pm), ("deputy", deputy) })
        {
            Assert.True(HttpStatusCode.Forbidden == (await client.GetAsync("/api/email-templates")).StatusCode, $"{who}: list");
            Assert.True(HttpStatusCode.Forbidden == (await client.GetAsync("/api/email-templates/ProjectAssigned")).StatusCode, $"{who}: get");
            Assert.True(HttpStatusCode.Forbidden == (await client.PutAsJsonAsync("/api/email-templates/ProjectAssigned", new { subject = "x", messageText = "x" })).StatusCode, $"{who}: update");
            Assert.True(HttpStatusCode.Forbidden == (await client.PostAsJsonAsync("/api/email-templates/ProjectAssigned/preview", new { })).StatusCode, $"{who}: preview");
            Assert.True(HttpStatusCode.Forbidden == (await client.PostAsJsonAsync("/api/email-templates/ProjectAssigned/test", new { to = "a@b.co" })).StatusCode, $"{who}: test");
            Assert.True(HttpStatusCode.Forbidden == (await client.PutAsJsonAsync("/api/email-settings/weekly-reminder", new { enabled = false })).StatusCode, $"{who}: schedule");
            var page = await client.GetAsync("/settings/email-templates");
            Assert.True(HttpStatusCode.Redirect == page.StatusCode, $"{who}: page is {(int)page.StatusCode}");
            Assert.NotEqual("/settings/email-templates", page.Headers.Location?.ToString());
        }
        Assert.Empty(sb.Smtp.Mails);

        // The administrator can.
        var list = await admin.GetAsync("/api/email-templates");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await JsonAsync(list);
        Assert.Equal(3, body.GetProperty("templates").GetArrayLength());
        Assert.True(body.GetProperty("variables").GetArrayLength() >= 11);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/settings/email-templates")).StatusCode);
    }

    [SqlFact]
    public async Task The_administrator_can_edit_a_template_and_the_next_email_uses_the_new_text()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();

        var save = await admin.PutAsJsonAsync("/api/email-templates/ProjectAssigned", new
        {
            nameAr = "إسناد (معدَّل)",
            subject = "مشروع جديد: {{ProjectName}}",
            messageText = "أهلًا {{UserName}}، مشروعك {{ProjectName}} جاهز.",
            isEnabled = true,
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var project = await sb.NewProjectAsync("مشروع التعديل");
        var (userId, _) = await sb.NewUserAsync("مستخدم");
        await Sandbox.AssignAsync(admin, project, userId);

        var mail = Assert.Single(sb.Smtp.Mails);
        Assert.Equal($"مشروع جديد: {Sandbox.Marker} مشروع التعديل", mail.Subject);
        Assert.Contains("أهلًا", mail.HtmlBody);
        Assert.Contains($"{Sandbox.BaseUrl}/meeting?project={project}", mail.HtmlBody);   // the fixed frame's button link, unaffected by the edited message text
    }

    // ── administration: validation and safety ───────────────────────────────
    [SqlFact]
    public async Task Unknown_variables_malformed_placeholders_and_html_in_the_subject_are_rejected_and_nothing_is_saved()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var before = (await Sandbox.QueryAsync("SELECT Subject, MessageText FROM EmailTemplates WHERE TemplateKey='ProjectAssigned'"))[0];

        var unknown = await admin.PutAsJsonAsync("/api/email-templates/ProjectAssigned", new { subject = "س {{Hack}}", messageText = "{{ProjectName}}" });
        var malformed = await admin.PutAsJsonAsync("/api/email-templates/ProjectAssigned", new { subject = "س", messageText = "{{ProjectName" });
        var htmlSubject = await admin.PutAsJsonAsync("/api/email-templates/ProjectAssigned", new { subject = "<b>س</b>", messageText = "x" });
        var inHref = await admin.PutAsJsonAsync("/api/email-templates/ProjectAssigned", new { subject = "س", messageText = "<a href=\"{{ProjectName}}\">x</a>" });
        var empty = await admin.PutAsJsonAsync("/api/email-templates/ProjectAssigned", new { subject = "", messageText = "" });

        foreach (var response in new[] { unknown, malformed, htmlSubject, inHref, empty }) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Hack", (await JsonAsync(unknown)).GetProperty("errors").GetProperty("subject").GetString());
        var after = (await Sandbox.QueryAsync("SELECT Subject, MessageText FROM EmailTemplates WHERE TemplateKey='ProjectAssigned'"))[0];
        Assert.Equal(before[0], after[0]);
        Assert.Equal(before[1], after[1]);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/email-templates/Nope")).StatusCode);
    }

    /// <summary>
    /// The administrator's message is plain text, not HTML: anything that looks like markup or a script is stored
    /// and sent back to the reader literally (HTML-encoded by the fixed frame), never interpreted or stripped.
    /// </summary>
    [SqlFact]
    public async Task Html_like_text_in_the_message_reaches_the_reader_literally_not_as_markup()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();

        var save = await admin.PutAsJsonAsync("/api/email-templates/ProjectAssigned", new
        {
            subject = "س {{ProjectName}}",
            messageText = "آمن {{UserName}} <script>alert(1)</script> onclick=\"steal()\"",
        });

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var json = await JsonAsync(save);
        Assert.Equal("آمن {{UserName}} <script>alert(1)</script> onclick=\"steal()\"", json.GetProperty("template").GetProperty("messageText").GetString());
        var inDatabase = (string)(await Sandbox.QueryAsync("SELECT MessageText FROM EmailTemplates WHERE TemplateKey='ProjectAssigned'"))[0][0]!;
        Assert.Contains("<script>", inDatabase);   // stored verbatim as text — encoding happens only when rendered into the e-mail

        var project = await sb.NewProjectAsync("مشروع الأمان");
        var (userId, _) = await sb.NewUserAsync("مستخدم");
        await Sandbox.AssignAsync(admin, project, userId);

        var mail = Assert.Single(sb.Smtp.Mails);
        Assert.DoesNotContain("<script", mail.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
        Assert.Contains("آمن", mail.HtmlBody);
    }

    [SqlFact]
    public async Task A_project_name_full_of_markup_reaches_the_reader_as_plain_text()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("<script>alert(1)</script><img src=x onerror=alert(2)>");
        var (userId, _) = await sb.NewUserAsync("مستخدم");

        await Sandbox.AssignAsync(admin, project, userId);

        var mail = Assert.Single(sb.Smtp.Mails);
        Assert.DoesNotContain("<script", mail.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", mail.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
    }

    [SqlFact]
    public async Task Preview_renders_sample_data_and_changes_nothing()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var projects = await Sandbox.ScalarAsync("SELECT COUNT(*) FROM projects");
        var assignments = await Sandbox.ScalarAsync("SELECT COUNT(*) FROM assignments");
        var logs = await Sandbox.ScalarAsync("SELECT COUNT(*) FROM EmailNotificationLog");
        var subjectBefore = (string)(await Sandbox.QueryAsync("SELECT Subject FROM EmailTemplates WHERE TemplateKey='ProjectAssigned'"))[0][0]!;

        var response = await admin.PostAsJsonAsync("/api/email-templates/ProjectAssigned/preview", new { subject = "معاينة {{ProjectName}}", messageText = "{{UserName}} — {{ProjectProgress}} — {{ProjectUrl}}<script>x()</script>" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.Equal("معاينة مشروع تجريبي — تطوير بوابة الخدمات", json.GetProperty("subject").GetString());
        var html = json.GetProperty("bodyHtml").GetString()!;
        Assert.Contains("أحمد محمد", html);
        Assert.Contains("45%", html);
        Assert.Contains(Sandbox.BaseUrl, html);
        Assert.DoesNotContain("<script", html);
        Assert.Equal(projects, await Sandbox.ScalarAsync("SELECT COUNT(*) FROM projects"));
        Assert.Equal(assignments, await Sandbox.ScalarAsync("SELECT COUNT(*) FROM assignments"));
        Assert.Equal(logs, await Sandbox.ScalarAsync("SELECT COUNT(*) FROM EmailNotificationLog"));
        Assert.Equal(subjectBefore, (string)(await Sandbox.QueryAsync("SELECT Subject FROM EmailTemplates WHERE TemplateKey='ProjectAssigned'"))[0][0]!);
        Assert.Empty(sb.Smtp.Mails);
    }

    [SqlFact]
    public async Task Test_email_sends_the_current_editor_content_to_the_given_address_only()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();

        var ok = await admin.PostAsJsonAsync("/api/email-templates/ProjectCompleted/test", new { to = "tester@test.example", subject = "تجربة {{ProjectName}}", messageText = "{{UserName}}" });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var mail = Assert.Single(sb.Smtp.Mails);
        Assert.Equal("tester@test.example", mail.To.Single());
        Assert.Equal("تجربة مشروع تجريبي — تطوير بوابة الخدمات", mail.Subject);
        Assert.Contains("أحمد محمد", mail.HtmlBody);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/email-templates/ProjectCompleted/test", new { to = "not-an-email" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/email-templates/ProjectCompleted/test", new { to = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/email-templates/ProjectCompleted/test", new { to = "a@b.co\r\nBcc: x@y.z" })).StatusCode);
        Assert.Single(sb.Smtp.Mails);

        sb.Smtp.Reject = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await admin.PostAsJsonAsync("/api/email-templates/ProjectCompleted/test", new { to = "tester@test.example" })).StatusCode);
    }

    [SqlFact]
    public async Task Deleting_a_project_removes_its_email_log()
    {
        await using var sb = await Sandbox.CreateAsync();
        var admin = await sb.AdminAsync();
        var project = await sb.NewProjectAsync("مشروع للحذف");
        var (userId, _) = await sb.NewUserAsync("مستخدم");
        await Sandbox.AssignAsync(admin, project, userId);
        Assert.Single(await Sandbox.LogAsync(project));

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/projects/{project}")).StatusCode);

        Assert.Empty(await Sandbox.LogAsync(project));
    }
}
