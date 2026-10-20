using System.Globalization;
using System.Text.RegularExpressions;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.ViewModels;

namespace ProjectTracker.Web.Services;

public sealed record HealthInput(
    string Status, int? Variance, bool IsOverdue, int? DaysToEnd, string? ReportedHealth, bool ReportIsStale, int? DaysSinceReport,
    int CriticalBlockers, int OpenCriticalRisks, int OverdueDecisions, AppSettingsVm Settings);

public sealed record AttentionInput(
    string Health, int OverdueDecisions, bool NeedsDecision, bool NeedsSupport, int HighRisks, bool IsStale, int? DaysToEnd,
    int? ActualProgress, AppSettingsVm Settings);

public sealed record MilestoneProgressInput(double Progress, int TasksCount, int DurationWeeks);

/// <summary>
/// Executive-dashboard calculations (port of lib/portfolio/calc.ts). Pure functions: dates are
/// yyyy-MM-dd strings and day differences are calendar days, so time zones never shift a result.
/// </summary>
public static partial class PortfolioCalc
{
    [GeneratedRegex(@"^(\d{4})-(\d{2})-(\d{2})")] private static partial Regex DatePrefix();

    private static DateOnly? ParseDateOnly(string? value)
    {
        if (value is null) return null;
        var m = DatePrefix().Match(value);
        if (!m.Success) return null;
        int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
        try { return new DateOnly(y, mo, d); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static string Format(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A valid yyyy-MM-dd (or ISO date-time prefix) normalised to yyyy-MM-dd; null otherwise.</summary>
    public static string? ToDateOnly(string? value)
    {
        if (value is null) return null;
        return ParseDateOnly(value.Trim()) is { } d ? Format(d) : null;
    }

    public static string TodayDateOnly() => Js.Today();

    /// <summary>to − from in calendar days; null when either date is not valid.</summary>
    public static int? DaysBetween(string from, string to) =>
        ParseDateOnly(from) is { } a && ParseDateOnly(to) is { } b ? b.DayNumber - a.DayNumber : null;

    public static string? AddDays(string date, int days) => ParseDateOnly(date) is { } d ? Format(d.AddDays(days)) : null;

    /// <summary>null without a start; 0 before the start; otherwise floor(days / 7) + 1 (no upper cap).</summary>
    public static int? ComputeElapsedWeek(string? startDate, string today)
    {
        if (string.IsNullOrEmpty(startDate)) return null;
        var days = DaysBetween(startDate, today);
        if (days is null) return null;
        if (days < 0) return 0;
        return days / 7 + 1;
    }

    /// <summary>Days until the end date, negative once exceeded; null without an end date.</summary>
    public static int? ComputeDaysToEnd(string? endDate, string today) =>
        string.IsNullOrEmpty(endDate) ? null : DaysBetween(today, endDate);

    public static (int? Value, string? Source) ComputeActualProgress(IReadOnlyList<MilestoneProgressInput> milestones, int? reportedActual)
    {
        var withTasks = milestones.Where(m => m.TasksCount > 0).ToList();
        if (withTasks.Count > 0)
        {
            double totalWeight = withTasks.Sum(m => Math.Max(1, m.DurationWeeks));
            var weighted = withTasks.Sum(m => m.Progress * Math.Max(1, m.DurationWeeks));
            return (Js.Round(weighted / totalWeight), "tasks");
        }
        return reportedActual is not null ? (reportedActual, "reported") : (null, null);
    }

    /// <summary>The planned progress is derived from the dates only (never typed by hand).</summary>
    public static (int? Value, string? Source) ComputePlannedProgress(string? startDate, string? endDate, int? totalWeeks, string today)
    {
        if (startDate is not null && totalWeeks is > 0)
        {
            var elapsedDays = DaysBetween(startDate, today);
            if (elapsedDays is null) return (null, null);
            if (elapsedDays < 0) return (0, "estimated");
            var scheduledDays = endDate is not null ? DaysBetween(startDate, endDate) : totalWeeks * 7 - 1;
            if (scheduledDays is null or < 0) return (null, null);
            if (scheduledDays == 0) return (100, "estimated");
            return (Js.Round((double)Math.Min(elapsedDays.Value, scheduledDays.Value) / scheduledDays.Value * 100), "estimated");
        }
        return (null, null);
    }

    public static int? ComputeVariance(int? actual, int? planned) => actual is null || planned is null ? null : actual - planned;

    public static string ClassifyVariance(int? variance, AppSettingsVm settings)
    {
        if (variance is null) return "unknown";
        if (variance >= settings.varianceOnTrack) return Fields.OnTrack;
        if (variance >= settings.varianceAttention) return Fields.NeedsAttention;
        return Fields.Delayed;
    }

    public static bool IsStaleReport(string? lastReportDate, string today, int staleDays)
    {
        if (string.IsNullOrEmpty(lastReportDate)) return true;
        var days = DaysBetween(lastReportDate, today);
        return days is null || days > staleDays;
    }

    private static int? PlannedProgress(int weekStart, int weekEnd, int? elapsedWeek)
    {
        if (elapsedWeek is not { } week) return null;
        if (week < weekStart) return 0;
        if (week > weekEnd) return 100;
        var span = weekEnd - weekStart + 1;
        return span > 0 ? Js.Round((double)(week - weekStart + 1) / span * 100) : 100;
    }

    public static (string State, int DelayWeeks, int? PlannedProgress) ComputeMilestoneState(int weekStart, int weekEnd, double progressRaw, int? elapsedWeek)
    {
        var progress = progressRaw;
        var planned = PlannedProgress(weekStart, weekEnd, elapsedWeek);

        if (progress >= 100) return (Fields.Completed, 0, planned);
        if (elapsedWeek is null) return (progress > 0 ? "in_progress" : "not_started", 0, null);
        if (elapsedWeek > weekEnd) return (Fields.Delayed, elapsedWeek.Value - weekEnd, planned);
        if (elapsedWeek < weekStart) return (progress > 0 ? "in_progress" : "not_started", 0, planned);
        return ("in_progress", 0, planned);
    }

    /// <summary>
    /// Milestones arrive ordered (week_start, then display_order). The one whose range covers the current week is the
    /// current milestone; the reported ids are only a fallback when no milestone is scheduled for this week.
    /// </summary>
    public static (MilestoneSummary? Current, MilestoneSummary? Next) PickCurrentAndNext(
        IReadOnlyList<MilestoneSummary> milestones, int? elapsedWeek, string? reportedCurrentId, string? reportedNextId)
    {
        MilestoneSummary? ById(string? id) => id is null ? null : milestones.FirstOrDefault(m => m.id == id);

        var scheduled = elapsedWeek is null
            ? null
            : milestones.FirstOrDefault(m => elapsedWeek >= m.week_start && elapsedWeek <= m.week_end);
        var current = scheduled ?? ById(reportedCurrentId) ?? milestones.FirstOrDefault(m => m.progress < 100);

        var next = scheduled is not null ? null : ById(reportedNextId);
        if (next is null && current is not null)
        {
            var idx = -1;
            for (var i = 0; i < milestones.Count; i++) if (ReferenceEquals(milestones[i], current)) { idx = i; break; }
            next = milestones.Skip(idx + 1).FirstOrDefault(m => m.progress < 100);
        }
        return (current, next);
    }

    private static readonly Dictionary<string, int> HealthSeverity = new()
    {
        [Fields.OnTrack] = 0, [Fields.NeedsAttention] = 1, [Fields.Delayed] = 2, [Fields.AtRisk] = 3, [Fields.Stopped] = 4,
    };

    private static readonly Dictionary<string, string> ReportedHealthLabel = new()
    {
        [Fields.OnTrack] = "ضمن الخطة", [Fields.NeedsAttention] = "يحتاج متابعة", [Fields.Delayed] = "متأخر", [Fields.AtRisk] = "متعثر", [Fields.Stopped] = "متوقف",
    };

    /// <summary>Number then noun in the correct Arabic form.</summary>
    internal static string DaysText(int n)
    {
        if (n == 1) return "يوم واحد";
        if (n == 2) return "يومان";
        var tail = n % 100;
        if (tail is >= 3 and <= 10) return $"{n} أيام";
        if (tail >= 11) return $"{n} يومًا";
        return $"{n} يوم";
    }

    /// <summary>"Point" after a signed number (the variance may be negative).</summary>
    internal static string PointsNoun(int n) => Math.Abs(n) % 100 is >= 3 and <= 10 ? "نقاط" : "نقطة";

    private sealed record Candidate(string Health, string? Reason);

    private static List<Candidate> HealthCandidates(HealthInput input)
    {
        var candidates = new List<Candidate>();
        if (input.CriticalBlockers > 0) candidates.Add(new(Fields.Stopped, "عائق حرج مفتوح يمنع التنفيذ"));
        if (input.ReportedHealth is not null && !input.ReportIsStale)
            candidates.Add(new(input.ReportedHealth, $"حسب آخر تحديث أسبوعي: {ReportedHealthLabel[input.ReportedHealth]}"));
        if (input.OverdueDecisions > 0) candidates.Add(new(Fields.AtRisk, "قرار مطلوب تجاوز موعده"));
        if (input.OpenCriticalRisks > 0) candidates.Add(new(Fields.AtRisk, "خطر أو تعثر حرج مفتوح"));
        if (input.IsOverdue)
            candidates.Add(new(Fields.Delayed, input.DaysToEnd is { } d ? $"متجاوز للمدة بـ {DaysText(Math.Abs(d))}" : "متجاوز للمدة"));
        if (input.Status == Fields.Delayed) candidates.Add(new(Fields.Delayed, "الحالة التشغيلية: متأخر"));

        var varianceClass = ClassifyVariance(input.Variance, input.Settings);
        if (varianceClass is Fields.Delayed or Fields.NeedsAttention)
            candidates.Add(new(varianceClass, $"الانحراف {input.Variance} {PointsNoun(input.Variance ?? 0)} عن الخطة"));
        else if (varianceClass == Fields.OnTrack)
            candidates.Add(new(Fields.OnTrack, null));
        return candidates;
    }

    public static (string Health, List<string> Reasons) ComputeHealth(HealthInput input)
    {
        if (input.Status == Fields.Completed) return (Fields.Completed, []);

        var candidates = HealthCandidates(input);

        string? staleReason = null;
        if (input.ReportIsStale)
            staleReason = input.DaysSinceReport is null ? "لم يُسجَّل أي تحديث أسبوعي" : $"آخر تحديث أسبوعي منذ {DaysText(input.DaysSinceReport.Value)}";

        if (candidates.Count == 0 && input.Variance is null)
        {
            var reasons = new List<string>();
            if (staleReason is not null) reasons.Add(staleReason);
            reasons.Add("لا تتوفر بيانات كافية لحساب الانحراف");
            return ("unknown", reasons);
        }

        if (staleReason is not null) candidates.Add(new(Fields.NeedsAttention, staleReason));

        // Stable order from most to least severe (earlier insertion first on ties).
        var ordered = candidates
            .Select((c, i) => (c, i))
            .OrderByDescending(x => HealthSeverity[x.c.Health])
            .ThenBy(x => x.i)
            .Select(x => x.c)
            .ToList();

        return (ordered[0].Health, ordered.Select(c => c.Reason).Where(r => r is not null).Select(r => r!).ToList());
    }

    public static (int? Rank, string? Reason) ComputeAttention(AttentionInput i)
    {
        if (i.Health == Fields.Completed) return (null, null);
        if (i.Health == Fields.Stopped || i.OverdueDecisions > 0 || i.NeedsDecision) return (1, "متوقف أو بانتظار قرار");
        if (i.Health is Fields.AtRisk or Fields.Delayed) return (2, "متأخر أو متعثر");
        if (i.NeedsSupport) return (2, "يحتاج دعمًا");
        if (i.HighRisks > 0) return (3, "خطر عالٍ مفتوح");
        if (i.IsStale) return (4, "لم يُحدَّث منذ مدة");
        if (i.DaysToEnd is >= 0 && i.DaysToEnd <= i.Settings.nearDeadlineDays && (i.ActualProgress ?? 0) < 80)
            return (5, "قريب من موعده بإنجاز منخفض");
        return (null, null);
    }

    private static int CompareNullableAsc(int? a, int? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return 1;
        if (b is null) return -1;
        return a.Value - b.Value;
    }

    /// <summary>A sorted copy: attention rank, then variance, then days to end (ascending, nulls last in each key). Stable.</summary>
    public static List<PortfolioProject> SortByAttention(IEnumerable<PortfolioProject> projects) =>
        projects.OrderBy(p => p, Comparer<PortfolioProject>.Create((a, b) =>
        {
            var c = CompareNullableAsc(a.attentionRank, b.attentionRank);
            if (c != 0) return c;
            c = CompareNullableAsc(a.variance, b.variance);
            return c != 0 ? c : CompareNullableAsc(a.daysToEnd, b.daysToEnd);
        })).ToList();

    public static PortfolioKpis ComputeKpis(IReadOnlyList<PortfolioProject> projects)
    {
        var withProgress = projects.Where(p => p.actualProgress is not null).ToList();
        return new PortfolioKpis
        {
            total = projects.Count,
            onTrack = projects.Count(p => p.health == Fields.OnTrack),
            needsAttention = projects.Count(p => p.health == Fields.NeedsAttention),
            delayedOrAtRisk = projects.Count(p => p.health is Fields.Delayed or Fields.AtRisk or Fields.Stopped),
            needsDecisionOrSupport = projects.Count(p => p.needsDecision || p.needsSupport),
            unknown = projects.Count(p => p.health == "unknown"),
            completed = projects.Count(p => p.health == Fields.Completed),
            averageProgress = withProgress.Count > 0 ? Js.Round(withProgress.Sum(p => (double)p.actualProgress!.Value) / withProgress.Count) : null,
        };
    }

    public static bool IsActionOverdue(string status, string? dueDate, string today)
    {
        if (status is Fields.Completed or "cancelled") return false;
        var due = ToDateOnly(dueDate);
        return due is not null && string.CompareOrdinal(due, today) < 0;
    }
}
