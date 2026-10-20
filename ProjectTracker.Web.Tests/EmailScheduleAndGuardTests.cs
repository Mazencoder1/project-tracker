using ProjectTracker.Web.Services.Email;
using ProjectTracker.Web.ViewModels;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>Pure rules: the weekly window, the once-per-week key, variable formatting, and the "no message text in code" guard.</summary>
public class EmailScheduleAndGuardTests
{
    private const string Riyadh = "Asia/Riyadh";
    private static readonly TimeOnly Eight = new(8, 0);

    private static DateTime Utc(string iso) => DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

    [Theory]
    [InlineData("2026-09-27T05:00:00Z", true)]    // Sunday 08:00 Riyadh — the moment it opens
    [InlineData("2026-09-27T04:59:59Z", false)]   // one second before
    [InlineData("2026-09-27T20:59:59Z", true)]    // still inside the 24h catch-up window
    [InlineData("2026-09-28T05:00:00Z", false)]   // exactly 24h later: closed
    [InlineData("2026-09-30T05:05:00Z", false)]   // Wednesday
    public void Window_opens_at_the_configured_day_and_time_in_the_system_time_zone(string utc, bool open) =>
        Assert.Equal(open, WeeklyReminderSchedule.OpenWindow(Utc(utc), Riyadh, day: 0, Eight) is not null);

    [Fact]
    public void Every_moment_of_one_window_belongs_to_the_same_ISO_week_and_the_next_week_is_different()
    {
        var early = WeeklyReminderSchedule.OpenWindow(Utc("2026-09-27T05:00:00Z"), Riyadh, 0, Eight)!;
        var late = WeeklyReminderSchedule.OpenWindow(Utc("2026-09-27T20:00:00Z"), Riyadh, 0, Eight)!;
        var next = WeeklyReminderSchedule.OpenWindow(Utc("2026-10-04T05:00:00Z"), Riyadh, 0, Eight)!;
        Assert.Equal((early.IsoYear, early.IsoWeek), (late.IsoYear, late.IsoWeek));
        Assert.NotEqual((early.IsoYear, early.IsoWeek), (next.IsoYear, next.IsoWeek));
    }

    [Fact]
    public void The_window_follows_the_administrators_day_and_time_not_a_fixed_one()
    {
        // Thursday 14:30 Riyadh = 11:30 UTC
        Assert.NotNull(WeeklyReminderSchedule.OpenWindow(Utc("2026-10-01T11:30:00Z"), Riyadh, day: 4, new TimeOnly(14, 30)));
        Assert.Null(WeeklyReminderSchedule.OpenWindow(Utc("2026-10-01T11:29:00Z"), Riyadh, day: 4, new TimeOnly(14, 30)));
        Assert.Null(WeeklyReminderSchedule.OpenWindow(Utc("2026-09-27T05:00:00Z"), Riyadh, day: 4, new TimeOnly(14, 30)));
    }

    [Fact]
    public void The_local_day_is_the_riyadh_day_not_the_servers()
    {
        // 22:00 UTC on Saturday is already Sunday 01:00 in Riyadh.
        Assert.Equal(DayOfWeek.Sunday, WeeklyReminderSchedule.ToLocal(Utc("2026-09-26T22:00:00Z"), Riyadh).DayOfWeek);
    }

    [Fact]
    public void Correlation_keys_have_the_documented_shape_and_are_case_insensitive_about_ids()
    {
        var project = Guid.Parse("AAAAAAAA-0000-0000-0000-000000000001");
        var user = Guid.Parse("BBBBBBBB-0000-0000-0000-000000000002");
        var assignment = Guid.Parse("CCCCCCCC-0000-0000-0000-000000000003");
        Assert.Equal($"ProjectAssigned:{project.ToString().ToLower()}:{user.ToString().ToLower()}:{assignment.ToString().ToLower()}", ProjectEmailNotifier.AssignedKey(project.ToString().ToUpper(), user.ToString().ToUpper(), assignment));
        Assert.Equal($"ProjectCompleted:{project.ToString().ToLower()}:{user.ToString().ToLower()}", ProjectEmailNotifier.CompletedKey(project.ToString().ToUpper(), user.ToString()));
        Assert.Equal($"ProjectWeeklyReminder:{project.ToString().ToLower()}:{user.ToString().ToLower()}:2026:39", ProjectEmailNotifier.WeeklyKey(project.ToString(), user.ToString(), 2026, 39));
    }

    [Theory]
    [InlineData(5, "5 أيام")]
    [InlineData(1, "يوم واحد")]
    [InlineData(0, "0 أيام")]
    [InlineData(-3, "متأخر 3 أيام")]
    [InlineData(null, "غير متوفر")]
    public void Days_remaining_reads_naturally_in_arabic(int? days, string expected) =>
        Assert.Equal(expected, ProjectEmailVariables.DaysRemaining(days));

    [Fact]
    public void Project_values_are_formatted_and_missing_data_shows_a_dash_not_null()
    {
        var project = new PortfolioProject { id = "1", name = "م", status = "delayed", actualProgress = 45, start_date = "2026-01-11", end_date = "2026-12-31", daysToEnd = 99, total_weeks = 20 };
        var values = ProjectEmailVariables.Build(project, "أحمد", "https://x.test/p/1", completedDate: "2026-12-20", overdueCount: 2, overdueNames: ["أ", "<b>ب</b>"]);
        Assert.Equal("45%", values["ProjectProgress"]);
        Assert.Equal("متأخر", values["ProjectStatus"]);
        Assert.Equal("99 يومًا", values["DaysRemaining"]);
        Assert.Equal("2026-12-20", values["ProjectCompletedDate"]);
        Assert.Equal("344 يومًا", values["ProjectDuration"]);                       // 11 Jan → 20 Dec, both days counted
        Assert.Equal("—", values["DepartmentName"]);
        Assert.Equal("—", values["ProjectManagerName"]);
        Assert.Contains("&lt;b&gt;ب&lt;/b&gt;", values["OverdueTasksList"]);      // names are encoded inside the ready-made list
        Assert.Equal("https://x.test/p/1", values["ProjectUrl"]);
        Assert.All(EmailTemplateCatalog.Variables, v => Assert.True(values.ContainsKey(v.Name), $"no value is produced for {{{{{v.Name}}}}}"));
    }

    [Fact]
    public void Every_catalogue_variable_has_a_sample_and_a_label()
    {
        Assert.All(EmailTemplateCatalog.Variables, v =>
        {
            Assert.False(string.IsNullOrWhiteSpace(v.Sample));
            Assert.False(string.IsNullOrWhiteSpace(v.LabelAr));
        });
        Assert.Equal(EmailTemplateCatalog.Variables.Count, EmailTemplateCatalog.Variables.Select(v => v.Name).Distinct().Count());
    }

    [Fact]
    public void Settings_time_format_is_strict_24_hour()
    {
        Assert.True(WeeklyReminderSettingsService.IsValidTime("08:00"));
        Assert.True(WeeklyReminderSettingsService.IsValidTime("23:59"));
        Assert.False(WeeklyReminderSettingsService.IsValidTime("8:00"));
        Assert.False(WeeklyReminderSettingsService.IsValidTime("24:00"));
        Assert.False(WeeklyReminderSettingsService.IsValidTime("12:60"));
        Assert.False(WeeklyReminderSettingsService.IsValidTime(""));
    }

    /// <summary>
    /// The rule of the feature: message wording lives in the EmailTemplates table only. If a sentence of a template
    /// ever appears in the notification code or the controllers that trigger it, this test fails.
    /// </summary>
    [Fact]
    public void No_email_wording_is_hard_coded_in_services_or_controllers()
    {
        var web = FindWebProject();
        // EmailTemplateShells.cs is deliberately excluded: it IS the fixed, non-editable HTML frame (see its own
        // doc comment) — an administrator never sees or edits it from the database, so the wording rule below does
        // not apply to it. The rule is about template content leaking into notification/controller code.
        var files = Directory.GetFiles(Path.Combine(web, "Services", "Email"), "*.cs")
            .Where(f => Path.GetFileName(f) != "EmailTemplateShells.cs")
            .Concat([Path.Combine(web, "Controllers", "EmailTemplatesController.cs"), Path.Combine(web, "Controllers", "ProjectsController.cs"), Path.Combine(web, "Controllers", "WorkBreakdownControllers.cs")]);
        string[] wording = ["مرحبًا", "مع التحية", "تم إسناد", "أُسند إليك", "تم إكمال", "اكتمل المشروع", "تذكير أسبوعي", "إسناد مشروع جديد", "رسالة آلية"];
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var phrase in wording)
                Assert.False(text.Contains(phrase), $"{Path.GetFileName(file)} contains the message wording \"{phrase}\" — it belongs in the EmailTemplates table.");
        }
    }

    [Fact]
    public void No_smtp_secret_or_localhost_link_address_is_committed_in_appsettings()
    {
        var web = FindWebProject();
        var settings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        // appsettings.json carries // comments explaining every entry (ASP.NET Core reads them; strict JSON does not).
        using var doc = System.Text.Json.JsonDocument.Parse(settings, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var email = doc.RootElement.GetProperty("Email");
        Assert.Equal("", email.GetProperty("Password").GetString());
        Assert.Equal("", email.GetProperty("Username").GetString());
        // Links in e-mails come from Application:BaseUrl / Notifications — never a localhost address.
        foreach (var section in new[] { "Application", "Email", "Notifications" })
            Assert.DoesNotContain("localhost", doc.RootElement.GetProperty(section).GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static string FindWebProject()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ProjectTracker.Web");
            if (File.Exists(Path.Combine(candidate, "ProjectTracker.Web.csproj"))) return candidate;
        }
        throw new DirectoryNotFoundException("ProjectTracker.Web project folder was not found above the test output.");
    }
}
