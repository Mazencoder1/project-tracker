using ProjectTracker.Web.Services;
using ProjectTracker.Web.ViewModels;
using Xunit;

namespace ProjectTracker.Web.Tests;

public class PortfolioExecutiveTests
{
    private static readonly AppSettingsVm Settings = new();

    private static PortfolioProject Project(Action<PortfolioProject>? tweak = null)
    {
        var p = new PortfolioProject { id = Guid.NewGuid().ToString(), name = "مشروع", status = "active", milestonesCount = 2, tasksCount = 10 };
        tweak?.Invoke(p);
        return p;
    }

    [Theory]
    [InlineData("completed", null, null, false, "completed")]
    [InlineData("active", 100, null, false, "completed")]
    [InlineData("active", 40, null, true, "delayed")]
    [InlineData("active", 40, -30, false, "delayed")]
    [InlineData("active", 40, -10, false, "needs_attention")]
    [InlineData("active", 40, 5, false, "on_track")]
    [InlineData("active", null, null, false, "unknown")]
    public void Executive_status_follows_completion_overdue_and_variance(string status, int? actual, int? variance, bool overdue, string expected)
    {
        var p = Project(x => { x.status = status; x.actualProgress = actual; x.variance = variance; x.isOverdue = overdue; });
        Assert.Equal(expected, PortfolioExecutive.ClassifyExecutiveStatus(p, Settings));
    }

    [Fact]
    public void Blockers_and_risky_health_make_a_project_stalled_and_health_labels_are_respected()
    {
        Assert.Equal("stalled", PortfolioExecutive.ClassifyExecutiveStatus(Project(x => x.criticalBlockers = 1), Settings));
        Assert.Equal("stalled", PortfolioExecutive.ClassifyExecutiveStatus(Project(x => x.health = "at_risk"), Settings));
        Assert.Equal("needs_attention", PortfolioExecutive.ClassifyExecutiveStatus(Project(x => x.health = "needs_attention"), Settings));
        Assert.Equal("on_track", PortfolioExecutive.ClassifyExecutiveStatus(Project(x => x.health = "on_track"), Settings));
        Assert.Equal("delayed", PortfolioExecutive.ClassifyExecutiveStatus(Project(x => x.status = "delayed"), Settings));
        Assert.Equal("delayed", PortfolioExecutive.ClassifyExecutiveStatus(Project(x => x.currentMilestone = new MilestoneSummary { state = "delayed", name = "م" }), Settings));
    }

    [Fact]
    public void Every_reason_for_attention_is_listed_in_priority_order()
    {
        var project = Project(p =>
        {
            p.isOverdue = true;
            p.daysToEnd = -5;
            p.delayedTasks = 4;
            p.tasksCount = 10;
            p.actualProgress = 20;
            p.variance = -40;
        });
        var support = new List<SupportRequest> { new() { id = "1", kind = "decision" }, new() { id = "2", kind = "support" } };
        var built = PortfolioExecutive.BuildExecutiveProject(project, support, Settings);

        Assert.Equal("delayed", built.execStatus);
        Assert.True(built.needsSupport);
        Assert.Equal(2, built.attentionScore);
        Assert.Contains(built.attentionReasons, r => r.Contains("تجاوز تاريخ النهاية"));
        Assert.Contains(built.attentionReasons, r => r.Contains("بانتظار قرار"));
        Assert.Contains(built.attentionReasons, r => r.Contains("المهام المتأخرة"));
    }

    [Fact]
    public void Each_delay_reason_has_its_own_wording_and_stalled_projects_quote_the_blocker()
    {
        string Reason(Action<PortfolioProject> tweak) =>
            PortfolioExecutive.BuildExecutiveProject(Project(p => { p.status = "delayed"; tweak(p); }), [], Settings).attentionReasons.First();

        Assert.Contains("الإنجاز أقل من المخطط", Reason(p => p.variance = -50));
        Assert.Contains("تجاوزت موعدها", Reason(p => p.currentMilestone = new MilestoneSummary { state = "delayed", name = "التصميم" }));
        Assert.Equal("سبب مخصص", Reason(p => p.healthReasons = ["سبب مخصص"]));
        Assert.Equal("الحالة التشغيلية: متأخر", Reason(_ => { }));

        var stalled = PortfolioExecutive.BuildExecutiveProject(Project(p => { p.criticalBlockers = 1; p.healthReasons = ["عائق حرج"]; }), [], Settings);
        Assert.StartsWith("متعثر — عائق حرج", stalled.attentionReasons.First());
        var noReason = PortfolioExecutive.BuildExecutiveProject(Project(p => p.criticalBlockers = 1), [], Settings);
        Assert.Contains("عائق أو خطر حرج مفتوح", noReason.attentionReasons.First());

        var attention = PortfolioExecutive.BuildExecutiveProject(Project(p => { p.health = "needs_attention"; p.variance = -10; }), [], Settings);
        Assert.Contains(attention.attentionReasons, r => r.Contains("أقل من المتوقع"));

        var near = PortfolioExecutive.BuildExecutiveProject(Project(p => { p.daysToEnd = 3; p.actualProgress = 20; p.variance = 0; }), [], Settings);
        Assert.True(near.nearDeadline);
        Assert.Contains(near.attentionReasons, r => r.Contains("ينتهي خلال"));
        var unknownProgress = PortfolioExecutive.BuildExecutiveProject(Project(p => p.daysToEnd = 3), [], Settings);
        Assert.Contains(unknownProgress.attentionReasons, r => r.Contains("غير متوفر"));
    }

    [Fact]
    public void Support_requests_come_from_the_update_open_actions_and_phase_notes()
    {
        var update = new ProjectUpdateVm
        {
            id = "u1", reporting_date = "2026-09-01", decision_required = "اعتماد", decision_from = "المدير", decision_needed_by = "2026-09-10",
            decision_status = "under_review", support_required = "دعم", support_from = "المالية", support_due_date = "2026-09-12",
        };
        var actions = new[]
        {
            new ActionSupportFields("a1", "support", "نص", "قسم", "2026-09-02", "2026-09-20", "new"),
            new ActionSupportFields("a2", "support", "منتهٍ", null, "2026-09-01", null, "completed"),
            new ActionSupportFields("a3", "other", "ليس دعمًا", null, "2026-09-01", null, "in_progress"),
        };
        var notes = new[] { new SupportNote("n1", "ملاحظة", "المرحلة 1"), new SupportNote("n2", "  ", "المرحلة 2") };

        var all = PortfolioExecutive.SupportRequestsFrom(update, actions, notes);
        Assert.Equal(["update-decision-u1", "update-support-u1", "action-a1", "note-n1"], all.Select(r => r.id));

        var taken = PortfolioExecutive.SupportRequestsFrom(new ProjectUpdateVm { id = "u2", decision_required = "x", decision_status = "taken" }, [], []);
        Assert.Empty(taken);
        Assert.Empty(PortfolioExecutive.SupportRequestsFrom(null, [], []));
    }

    [Fact]
    public void Sorting_kpis_departments_and_trend_are_computed_from_the_built_projects()
    {
        ExecutiveProject P(string name, int? score, int? variance, int? days, string status = "on_track", int? progress = 50, string? department = "إدارة أ", bool? compliant = true) =>
            new() { id = name, name = name, attentionScore = score, variance = variance, daysToEnd = days, execStatus = status, actualProgress = progress, department = department, scheduleCompliant = compliant };

        var sorted = PortfolioExecutive.SortExecutiveProjects([
            P("ج", null, null, null), P("ب", 2, -5, 10), P("أ", 1, 0, 3), P("د", 2, -5, 4), P("هـ", 2, -9, 4), P("و", 2, -5, 4),
        ]);
        Assert.Equal("أ", sorted[0].name);
        Assert.Equal("ج", sorted[^1].name);

        var kpis = PortfolioExecutive.ComputeExecutiveKpis([
            P("1", 1, 0, 1, "delayed", 30, "إدارة ب", false), P("2", null, 5, 9, "completed", 100, null, null), P("3", null, null, null, "unknown", null),
        ]);
        Assert.Equal(3, kpis.total);
        Assert.Equal(1, kpis.completed);
        Assert.Equal(2, kpis.progressAvailable);
        Assert.Equal(65, kpis.averageProgress);

        var empty = PortfolioExecutive.ComputeExecutiveKpis([]);
        Assert.Null(empty.averageProgress);
        Assert.Null(empty.scheduleCompliance);

        var departments = PortfolioExecutive.ComputeDepartmentProgress([P("1", null, null, null, progress: 80), P("2", null, null, null, progress: 40, department: null), P("3", null, null, null, progress: null)]);
        Assert.Equal(2, departments.Count);
        Assert.Equal(80, departments[0].averageProgress);

        var history = new[]
        {
            new PortfolioExecutive.HistoryPoint("P1", "2026-06-10", 20), new PortfolioExecutive.HistoryPoint("P1", "2026-08-02", 60),
            new PortfolioExecutive.HistoryPoint("P2", "2026-08-15", 40), new PortfolioExecutive.HistoryPoint("P2", "2026-12-01", 99),
            new PortfolioExecutive.HistoryPoint("P3", "bad-date", 10), new PortfolioExecutive.HistoryPoint("P3", "2026-08-20", null),
        };
        var trend = PortfolioExecutive.ComputeProgressTrend(history, "2026-09-05");
        Assert.NotEmpty(trend);
        Assert.Empty(PortfolioExecutive.ComputeProgressTrend([new PortfolioExecutive.HistoryPoint("P1", "2026-08-02", 60)], "2026-09-05"));
    }
}
