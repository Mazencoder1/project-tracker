using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Services;
using ProjectTracker.Web.ViewModels;
using Xunit;

namespace ProjectTracker.Web.Tests;

/// <summary>The pure business rules ported from lib/portfolio/calc.ts, validation.ts and executive.ts.</summary>
public class PortfolioRulesTests
{
    private static readonly AppSettingsVm Settings = new();   // 14 days, -5, -15, 30

    [Theory]
    [InlineData(0, "on_track")]
    [InlineData(-5, "on_track")]
    [InlineData(-6, "needs_attention")]
    [InlineData(-15, "needs_attention")]
    [InlineData(-16, "delayed")]
    public void Variance_thresholds(int variance, string expected) =>
        Assert.Equal(expected, PortfolioCalc.ClassifyVariance(variance, Settings));

    [Fact]
    public void Variance_unknown_without_data() => Assert.Equal("unknown", PortfolioCalc.ClassifyVariance(null, Settings));

    [Theory]
    [InlineData("2026-01-01", "2025-12-31", 0)]   // before the start
    [InlineData("2026-01-01", "2026-01-01", 1)]   // first day
    [InlineData("2026-01-01", "2026-01-07", 1)]   // seventh day
    [InlineData("2026-01-01", "2026-01-08", 2)]
    [InlineData("2026-01-01", "2026-03-01", 9)]   // no upper cap
    public void Elapsed_week(string start, string today, int expected) => Assert.Equal(expected, PortfolioCalc.ComputeElapsedWeek(start, today));

    [Fact]
    public void Elapsed_week_is_null_without_a_start() => Assert.Null(PortfolioCalc.ComputeElapsedWeek(null, "2026-01-01"));

    [Fact]
    public void Days_to_end_is_negative_once_exceeded()
    {
        Assert.Equal(-3, PortfolioCalc.ComputeDaysToEnd("2026-01-07", "2026-01-10"));
        Assert.Equal(5, PortfolioCalc.ComputeDaysToEnd("2026-01-15", "2026-01-10"));
    }

    [Fact]
    public void Planned_progress_comes_from_the_dates_only()
    {
        // 14 weeks = 98 days; start Jan 1, end Apr 8 (97 days later) → halfway on Feb 18.
        Assert.Equal((0, "estimated"), PortfolioCalc.ComputePlannedProgress("2026-01-10", "2026-04-17", 14, "2026-01-01"));
        Assert.Equal((100, "estimated"), PortfolioCalc.ComputePlannedProgress("2026-01-01", "2026-04-08", 14, "2027-01-01"));
        Assert.Equal((null, null), PortfolioCalc.ComputePlannedProgress(null, null, 14, "2026-01-01"));
    }

    [Fact]
    public void Actual_progress_is_weighted_by_duration_and_prefers_tasks()
    {
        var value = PortfolioCalc.ComputeActualProgress(
            [new MilestoneProgressInput(100, 2, 1), new MilestoneProgressInput(0, 3, 3)], 50);
        Assert.Equal((25, "tasks"), value);   // (100*1 + 0*3) / 4
        Assert.Equal((50, "reported"), PortfolioCalc.ComputeActualProgress([], 50));
        Assert.Equal((null, null), PortfolioCalc.ComputeActualProgress([], null));
    }

    [Fact]
    public void Js_round_rounds_halves_up_like_Math_round()
    {
        Assert.Equal(3, Js.Round(2.5));
        Assert.Equal(-2, Js.Round(-2.5));   // JS: Math.round(-2.5) === -2
        Assert.Equal(1, Js.Round(0.5));
    }

    [Fact]
    public void Milestone_state_follows_the_current_week()
    {
        // The milestone covers weeks 3..4.
        Assert.Equal(("in_progress", 0, 50), PortfolioCalc.ComputeMilestoneState(3, 4, 10, 3));
        Assert.Equal(("delayed", 2, 100), PortfolioCalc.ComputeMilestoneState(3, 4, 10, 6));
        Assert.Equal(("not_started", 0, 0), PortfolioCalc.ComputeMilestoneState(3, 4, 0, 1));
        Assert.Equal(("in_progress", 0, 0), PortfolioCalc.ComputeMilestoneState(3, 4, 5, 1));
        Assert.Equal(("not_started", 0, null), PortfolioCalc.ComputeMilestoneState(3, 4, 0, null));
    }

    [Fact]
    public void Completed_milestone_is_completed_even_after_its_end()
    {
        var (state, delay, planned) = PortfolioCalc.ComputeMilestoneState(1, 2, 100, 9);
        Assert.Equal(("completed", 0, 100), (state, delay, planned));
    }

    [Fact]
    public void Health_completed_project_has_no_reasons()
    {
        var (health, reasons) = PortfolioCalc.ComputeHealth(Input(status: "completed"));
        Assert.Equal("completed", health);
        Assert.Empty(reasons);
    }

    [Fact]
    public void Health_takes_the_most_severe_candidate_and_keeps_all_reasons()
    {
        var (health, reasons) = PortfolioCalc.ComputeHealth(Input(criticalBlockers: 1, variance: -20));
        Assert.Equal("stopped", health);
        Assert.Equal("عائق حرج مفتوح يمنع التنفيذ", reasons[0]);
        Assert.Contains(reasons, r => r.StartsWith("الانحراف -20"));
    }

    [Fact]
    public void Health_is_unknown_without_any_signal()
    {
        var (health, reasons) = PortfolioCalc.ComputeHealth(Input(reportIsStale: true, daysSinceReport: null));
        Assert.Equal("unknown", health);
        Assert.Equal(["لم يُسجَّل أي تحديث أسبوعي", "لا تتوفر بيانات كافية لحساب الانحراف"], reasons);
    }

    [Fact]
    public void Attention_priorities()
    {
        Assert.Equal(1, PortfolioCalc.ComputeAttention(Attention(health: "stopped")).Rank);
        Assert.Equal(1, PortfolioCalc.ComputeAttention(Attention(needsDecision: true)).Rank);
        Assert.Equal(2, PortfolioCalc.ComputeAttention(Attention(health: "delayed")).Rank);
        Assert.Equal(3, PortfolioCalc.ComputeAttention(Attention(highRisks: 1)).Rank);
        Assert.Equal(4, PortfolioCalc.ComputeAttention(Attention(isStale: true)).Rank);
        Assert.Equal(5, PortfolioCalc.ComputeAttention(Attention(daysToEnd: 10, actualProgress: 50)).Rank);
        Assert.Null(PortfolioCalc.ComputeAttention(Attention(health: "completed", needsDecision: true)).Rank);
    }

    [Fact]
    public void Executive_status_is_exclusive_and_follows_the_documented_order()
    {
        Assert.Equal("completed", PortfolioExecutive.ClassifyExecutiveStatus(Project(status: "completed"), Settings));
        Assert.Equal("stalled", PortfolioExecutive.ClassifyExecutiveStatus(Project(criticalBlockers: 1), Settings));
        Assert.Equal("delayed", PortfolioExecutive.ClassifyExecutiveStatus(Project(isOverdue: true), Settings));
        Assert.Equal("delayed", PortfolioExecutive.ClassifyExecutiveStatus(Project(variance: -16), Settings));
        Assert.Equal("needs_attention", PortfolioExecutive.ClassifyExecutiveStatus(Project(variance: -6), Settings));
        Assert.Equal("on_track", PortfolioExecutive.ClassifyExecutiveStatus(Project(variance: -5), Settings));
        Assert.Equal("unknown", PortfolioExecutive.ClassifyExecutiveStatus(Project(), Settings));
    }

    // ── validation ──────────────────────────────────────────────────────────
    [Fact]
    public void Weekly_update_requires_a_reason_for_delayed_health()
    {
        var result = PortfolioValidation.ValidateProjectUpdate(new()
        {
            ["reporting_date"] = "2026-01-05", ["health_status"] = "delayed", ["actual_progress"] = 101L,
        }, "2026-01-10");
        Assert.False(result.Ok);
        Assert.Equal("سبب التأخير أو التعثر مطلوب لهذه الحالة", result.Errors["delay_reason"]);
        Assert.Equal("يجب أن تكون نسبة صحيحة بين 0 و100", result.Errors["actual_progress"]);
    }

    [Fact]
    public void Weekly_update_rejects_a_future_date_and_defaults_the_decision_status()
    {
        var future = PortfolioValidation.ValidateProjectUpdate(new() { ["reporting_date"] = "2026-02-01", ["health_status"] = "on_track" }, "2026-01-10");
        Assert.Equal("لا يمكن أن يكون تاريخ التقرير بعد اليوم", future.Errors["reporting_date"]);

        var ok = PortfolioValidation.ValidateProjectUpdate(new()
        {
            ["reporting_date"] = "2026-01-05", ["health_status"] = "on_track", ["decision_required"] = "اعتماد", ["decision_needed_by"] = "2026-01-20",
        }, "2026-01-10");
        Assert.True(ok.Ok);
        Assert.Equal("new", ok.Value.decision_status);
    }

    [Fact]
    public void Risk_validation_trims_and_enforces_limits()
    {
        var bad = PortfolioValidation.ValidateRisk(new() { ["type"] = "risk", ["title"] = "ab", ["severity"] = "huge" });
        Assert.Equal("العنوان قصير جدًا (3 أحرف على الأقل)", bad.Errors["title"]);
        Assert.Equal("قيمة غير مسموح بها", bad.Errors["severity"]);

        var good = PortfolioValidation.ValidateRisk(new() { ["type"] = "blocker", ["title"] = "  عائق  ", ["severity"] = "critical" });
        Assert.True(good.Ok);
        Assert.Equal("عائق", good.Value["title"]);
        Assert.Equal("open", good.Value["status"]);
    }

    [Fact]
    public void Completing_an_action_needs_closure_notes()
    {
        var result = PortfolioValidation.ValidateMeetingAction(new() { ["status"] = "completed" }, partial: true);
        Assert.Equal("ملاحظات الإغلاق مطلوبة عند إكمال الإجراء", result.Errors["closure_notes"]);
    }

    [Fact]
    public void App_settings_keep_attention_below_on_track()
    {
        var result = PortfolioValidation.ValidateAppSettings(new()
        {
            ["staleUpdateDays"] = 14L, ["varianceOnTrack"] = -10L, ["varianceAttention"] = -5L, ["nearDeadlineDays"] = 30L,
        });
        Assert.False(result.Ok);
        Assert.Equal("يجب أن تكون القيمة بين -100 و-10", result.Errors["varianceAttention"]);
    }

    // ── project rules ───────────────────────────────────────────────────────
    [Theory]
    [InlineData("2026-01-01", "2026-01-01", 1)]
    [InlineData("2026-01-01", "2026-01-07", 1)]
    [InlineData("2026-01-01", "2026-01-08", 2)]
    [InlineData("2026-01-01", "2026-04-08", 14)]
    public void Project_weeks_round_up(string start, string end, int weeks) => Assert.Equal(weeks, ProjectRules.CalcProjectWeeks(start, end));

    [Fact]
    public void Project_weeks_reject_invalid_or_reversed_dates()
    {
        Assert.Null(ProjectRules.CalcProjectWeeks("2026-02-30", "2026-03-01"));
        Assert.Null(ProjectRules.CalcProjectWeeks("2026-03-02", "2026-03-01"));
    }

    [Fact]
    public void Phase_colors_snap_to_the_approved_palette()
    {
        Assert.Equal("#0B213D", ProjectRules.PhaseColor(null));
        Assert.Equal("#143A6C", ProjectRules.PhaseColor("#143a6c"));
        Assert.Equal("#9B2226", ProjectRules.PhaseColor("#ff0000"));
        Assert.Equal("#0B213D", ProjectRules.PhaseColor("not-a-color"));
    }

    [Theory]
    [InlineData("0512345678", "966512345678")]
    [InlineData("+966512345678", "966512345678")]
    [InlineData("00966512345678", "966512345678")]
    [InlineData("٠٥١٢٣٤٥٦٧٨", "966512345678")]
    public void Saudi_mobile_numbers_are_normalised(string raw, string expected) => Assert.Equal(expected, OtpRules.NormalizeMobile(raw));

    [Theory]
    [InlineData("0412345678")]
    [InlineData("12345")]
    [InlineData("")]
    public void Other_numbers_are_not_mobiles(string raw) => Assert.Null(OtpRules.NormalizeMobile(raw));

    [Fact]
    public void Otp_lifecycle_rules()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var fresh = new OtpRules.Row(now.AddMinutes(5), 0, null, 0, now);
        Assert.Equal(OtpRules.State.Ok, OtpRules.GetState(fresh, now));
        Assert.Equal(OtpRules.State.Expired, OtpRules.GetState(fresh with { ExpiresAt = now.AddSeconds(-1) }, now));
        Assert.Equal(OtpRules.State.Locked, OtpRules.GetState(fresh with { Attempts = 5 }, now));
        Assert.Equal(OtpRules.State.Used, OtpRules.GetState(fresh with { ConsumedAt = now }, now));
        Assert.Equal(60, OtpRules.CanResend(fresh, now).WaitSec);
        Assert.True(OtpRules.CanResend(fresh with { LastSentAt = now.AddSeconds(-61) }, now).Ok);
        Assert.Equal("too_many", OtpRules.CanResend(fresh with { Resends = 3, LastSentAt = now.AddHours(-1) }, now).Reason);
    }

    [Fact]
    public void Weekly_digest_is_due_on_sunday_after_the_configured_hour_in_the_business_time_zone()
    {
        // 2026-09-20 is a Sunday. 03:30 UTC = 06:30 in Riyadh (before 07:00), 04:30 UTC = 07:30.
        var early = NotifyRules.ZonedNowIn(new DateTime(2026, 9, 20, 3, 30, 0, DateTimeKind.Utc), "Asia/Riyadh");
        var due = NotifyRules.ZonedNowIn(new DateTime(2026, 9, 20, 4, 30, 0, DateTimeKind.Utc), "Asia/Riyadh");
        Assert.False(NotifyRules.IsWeeklyDue(early));
        Assert.True(NotifyRules.IsWeeklyDue(due));
        Assert.Equal("2026-09-20", NotifyRules.WeekRef(due));
        // Saturday evening in UTC is already Sunday in Riyadh.
        var lateSaturdayUtc = NotifyRules.ZonedNowIn(new DateTime(2026, 9, 19, 21, 30, 0, DateTimeKind.Utc), "Asia/Riyadh");
        Assert.Equal(0, lateSaturdayUtc.Weekday);
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private static HealthInput Input(string status = "active", int? variance = null, bool isOverdue = false, int? daysToEnd = null,
        string? reportedHealth = null, bool reportIsStale = false, int? daysSinceReport = 0, int criticalBlockers = 0) =>
        new(status, variance, isOverdue, daysToEnd, reportedHealth, reportIsStale, daysSinceReport, criticalBlockers, 0, 0, Settings);

    private static AttentionInput Attention(string health = "on_track", int overdueDecisions = 0, bool needsDecision = false,
        bool needsSupport = false, int highRisks = 0, bool isStale = false, int? daysToEnd = null, int? actualProgress = null) =>
        new(health, overdueDecisions, needsDecision, needsSupport, highRisks, isStale, daysToEnd, actualProgress, Settings);

    private static PortfolioProject Project(string status = "active", int criticalBlockers = 0, bool isOverdue = false, int? variance = null) => new()
    {
        status = status, criticalBlockers = criticalBlockers, isOverdue = isOverdue, variance = variance, health = "unknown",
    };
}
