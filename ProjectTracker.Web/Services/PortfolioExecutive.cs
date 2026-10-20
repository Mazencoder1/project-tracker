using System.Globalization;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.ViewModels;

namespace ProjectTracker.Web.Services;

/// <summary>An open meeting action, reduced to what support-request collection needs.</summary>
public sealed record ActionSupportFields(string Id, string ActionType, string Description, string? SupportingDepartment, string MeetingDate, string? DueDate, string Status);

/// <summary>A Fields.Support note on a milestone.</summary>
public sealed record SupportNote(string Id, string Content, string? MilestoneName);

/// <summary>
/// Executive-status classification and dashboard aggregates (port of lib/portfolio/executive.ts).
/// Anything that cannot be computed stays null; nothing is invented.
/// </summary>
public static class PortfolioExecutive
{
    public const int HighDelayedTasks = 5;
    public const double HighDelayedTasksRatio = 0.25;
    public const int NearDeadlineLowProgress = 80;

    private static readonly System.Globalization.CompareInfo Arabic = new CultureInfo("ar").CompareInfo;

    /// <summary>Counted noun after a number (3–10 plural, 11+ singular accusative...).</summary>
    private static string Counted(int n, string one, string two, string few, string many)
    {
        if (n == 1) return one;
        if (n == 2) return two;
        var tail = n % 100;
        if (tail is >= 3 and <= 10) return $"{n} {few}";
        if (tail >= 11) return $"{n} {many}";
        return $"{n} {one}";
    }

    private static string DaysText(int n) => Counted(n, "يوم واحد", "يومان", "أيام", "يومًا");
    private static string OpenRequestsText(int n) => Counted(n, "طلب مفتوح", "طلبان مفتوحان", "طلبات مفتوحة", "طلبًا مفتوحًا");
    private static string PointsNoun(int n) => Math.Abs(n) % 100 is >= 3 and <= 10 ? "نقاط" : "نقطة";

    /// <summary>Open support requests of one project from its three real sources.</summary>
    public static List<SupportRequest> SupportRequestsFrom(ProjectUpdateVm? latest, IEnumerable<ActionSupportFields> actions, IEnumerable<SupportNote> supportNotes)
    {
        var requests = new List<SupportRequest>();
        if (DecisionFromUpdate(latest) is { } decision) requests.Add(decision);
        if (SupportFromUpdate(latest) is { } support) requests.Add(support);
        requests.AddRange(actions
            .Where(a => a.Status is "new" or "in_progress" && a.ActionType is Fields.Support or "decision" or "escalation")
            .OrderBy(a => a.DueDate ?? "9999", StringComparer.Ordinal)
            .Select(a => new SupportRequest
            {
                id = $"action-{a.Id}",
                kind = a.ActionType,
                source = "meeting_action",
                description = a.Description,
                party = NullIfBlank(a.SupportingDepartment),
                requestedAt = string.IsNullOrEmpty(a.MeetingDate) ? null : a.MeetingDate,
                dueDate = a.DueDate,
                status = a.Status,
            }));
        requests.AddRange(supportNotes
            .Where(note => !string.IsNullOrEmpty(note.Content?.Trim()))
            .Select(note => new SupportRequest
            {
                id = $"note-{note.Id}",
                kind = Fields.Support,
                source = "phase_note",
                description = note.Content?.Trim() ?? "",
                status = "open",
                milestoneName = note.MilestoneName,
            }));
        return requests;
    }

    private static SupportRequest? DecisionFromUpdate(ProjectUpdateVm? latest)
    {
        if (latest?.decision_required?.Trim() is not { Length: > 0 } decision || latest.decision_status == "taken") return null;
        return new SupportRequest
        {
            id = $"update-decision-{latest.id}",
            kind = "decision",
            source = "weekly_update",
            description = decision,
            party = NullIfBlank(latest.decision_from),
            requestedAt = string.IsNullOrEmpty(latest.reporting_date) ? null : latest.reporting_date,
            dueDate = latest.decision_needed_by,
            status = latest.decision_status ?? "new",
        };
    }

    private static SupportRequest? SupportFromUpdate(ProjectUpdateVm? latest)
    {
        if (latest?.support_required?.Trim() is not { Length: > 0 } support) return null;
        return new SupportRequest
        {
            id = $"update-support-{latest.id}",
            kind = Fields.Support,
            source = "weekly_update",
            description = support,
            party = NullIfBlank(latest.support_from),
            requestedAt = string.IsNullOrEmpty(latest.reporting_date) ? null : latest.reporting_date,
            dueDate = latest.support_due_date,
            status = "open",
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Exclusive executive statuses (they add up to the number of projects): completed, stalled, delayed,
    /// on_track / needs_attention by variance, or unknown when nothing can be judged.
    /// </summary>
    public static string ClassifyExecutiveStatus(PortfolioProject p, AppSettingsVm settings)
    {
        if (p.status == Fields.Completed || (p.actualProgress == 100 && p.milestonesCount > 0)) return Fields.Completed;
        if (p.criticalBlockers > 0 || p.health is "at_risk" or "stopped") return "stalled";
        if (p.isOverdue || p.status == Fields.Delayed || p.health == Fields.Delayed
            || (p.variance is { } v && v < settings.varianceAttention)
            || p.currentMilestone?.state == Fields.Delayed)
            return Fields.Delayed;
        if (p.health == Fields.NeedsAttention) return Fields.NeedsAttention;
        if (p.variance is { } variance) return variance >= settings.varianceOnTrack ? Fields.OnTrack : Fields.NeedsAttention;
        if (p.health == Fields.OnTrack) return Fields.OnTrack;
        return "unknown";
    }

    private static bool HasHighDelayedTasks(int delayedTasks, int tasksCount) =>
        delayedTasks >= HighDelayedTasks || (delayedTasks >= 2 && tasksCount > 0 && (double)delayedTasks / tasksCount >= HighDelayedTasksRatio);

    private static string DelayedReason(PortfolioProject p, AppSettingsVm settings)
    {
        if (p.isOverdue && p.daysToEnd is { } d) return $"تجاوز تاريخ النهاية بـ {DaysText(Math.Abs(d))}";
        if (p.variance is { } v && v < settings.varianceAttention) return $"الإنجاز أقل من المخطط بـ {Math.Abs(v)} {PointsNoun(v)}";
        if (p.currentMilestone?.state == Fields.Delayed) return $"مرحلة «{p.currentMilestone.name}» تجاوزت موعدها";
        if (p.healthReasons.Count > 0 && !string.IsNullOrEmpty(p.healthReasons[0])) return p.healthReasons[0];
        return "الحالة التشغيلية: متأخر";
    }

    /// <summary>Attention reasons with a priority score (lower = more important), in the order shown to the deputy.</summary>
    private static List<(int Score, string Text)> AttentionReasons(
        PortfolioProject p, string execStatus, List<SupportRequest> supportRequests, AppSettingsVm settings, bool nearDeadline)
    {
        var completed = execStatus == Fields.Completed;
        var reasons = new List<(int Score, string Text)>();
        if (execStatus == "stalled") reasons.Add((1, $"متعثر — {(p.healthReasons.Count > 0 ? p.healthReasons[0] : "عائق أو خطر حرج مفتوح")}"));
        if (execStatus == Fields.Delayed) reasons.Add((2, DelayedReason(p, settings)));
        if (supportRequests.Count > 0)
        {
            var decisions = supportRequests.Count(r => r.kind != Fields.Support);
            reasons.Add((3, decisions > 0
                ? $"بانتظار قرار أو تصعيد ({OpenRequestsText(supportRequests.Count)})"
                : $"دعم مطلوب ({OpenRequestsText(supportRequests.Count)})"));
        }
        if (execStatus == Fields.NeedsAttention && p.variance is { } gap)
            reasons.Add((4, $"الإنجاز أقل من المتوقع بـ {Math.Abs(gap)} {PointsNoun(gap)}"));
        if (!completed && HasHighDelayedTasks(p.delayedTasks, p.tasksCount))
            reasons.Add((5, $"عدد مرتفع من المهام المتأخرة ({p.delayedTasks} من {p.tasksCount})"));
        if (nearDeadline && (p.actualProgress ?? 0) < NearDeadlineLowProgress)
            reasons.Add((6, $"ينتهي خلال {DaysText(p.daysToEnd ?? 0)} والإنجاز {(p.actualProgress is null ? "غير متوفر" : $"{p.actualProgress}%")}"));
        return reasons;
    }

    public static ExecutiveProject BuildExecutiveProject(PortfolioProject p, List<SupportRequest> supportRequests, AppSettingsVm settings)
    {
        var execStatus = ClassifyExecutiveStatus(p, settings);
        var completed = execStatus == Fields.Completed;
        var nearDeadline = !completed && p.daysToEnd is >= 0 && p.daysToEnd <= settings.nearDeadlineDays;
        var needsSupport = supportRequests.Count > 0;
        bool? scheduleCompliant = completed || p.variance is null ? null : p.variance >= settings.varianceOnTrack && !p.isOverdue;

        var reasons = AttentionReasons(p, execStatus, supportRequests, settings, nearDeadline);

        return new ExecutiveProject
        {
            id = p.id,
            name = p.name,
            description = p.description,
            project_code = p.project_code,
            department = p.department,
            status = p.status,
            start_date = p.start_date,
            end_date = p.end_date,
            total_weeks = p.total_weeks,
            managers = p.managers.Select(m => m.full_name).ToList(),
            execStatus = execStatus,
            actualProgress = p.actualProgress,
            plannedProgress = p.plannedProgress,
            plannedEstimated = p.plannedSource == "estimated",
            variance = p.variance,
            daysToEnd = p.daysToEnd,
            isOverdue = p.isOverdue,
            currentMilestoneName = p.currentMilestone?.name,
            tasksCount = p.tasksCount,
            delayedTasks = p.delayedTasks,
            supportRequests = supportRequests,
            needsSupport = needsSupport,
            nearDeadline = nearDeadline,
            scheduleCompliant = scheduleCompliant,
            attentionReasons = reasons.Select(r => r.Text).ToList(),
            attentionScore = reasons.Count > 0 ? reasons.Min(r => r.Score) : null,
        };
    }

    private static int CompareNullable(int? a, int? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return 1;
        if (b is null) return -1;
        return a.Value - b.Value;
    }

    /// <summary>Highest priority first, then the worst variance, then the nearest deadline, then name.</summary>
    public static List<ExecutiveProject> SortExecutiveProjects(IEnumerable<ExecutiveProject> projects) =>
        projects.OrderBy(p => p, Comparer<ExecutiveProject>.Create((a, b) =>
        {
            var c = CompareNullable(a.attentionScore, b.attentionScore);
            if (c != 0) return c;
            c = CompareNullable(a.variance, b.variance);
            if (c != 0) return c;
            c = CompareNullable(a.daysToEnd, b.daysToEnd);
            return c != 0 ? c : Arabic.Compare(a.name, b.name, CompareOptions.None);
        })).ToList();

    public static ExecutiveKpis ComputeExecutiveKpis(IReadOnlyList<ExecutiveProject> projects)
    {
        var withProgress = projects.Where(p => p.actualProgress is not null).ToList();
        var eligible = projects.Where(p => p.scheduleCompliant is not null).ToList();
        var compliant = eligible.Count(p => p.scheduleCompliant == true);
        return new ExecutiveKpis
        {
            total = projects.Count,
            completed = projects.Count(p => p.execStatus == Fields.Completed),
            onTrack = projects.Count(p => p.execStatus == Fields.OnTrack),
            needsAttention = projects.Count(p => p.execStatus == Fields.NeedsAttention),
            delayed = projects.Count(p => p.execStatus == Fields.Delayed),
            stalled = projects.Count(p => p.execStatus == "stalled"),
            unknown = projects.Count(p => p.execStatus == "unknown"),
            needsSupport = projects.Count(p => p.needsSupport),
            nearDeadline = projects.Count(p => p.nearDeadline),
            delayedTasks = projects.Sum(p => p.delayedTasks),
            projectsWithDelayedTasks = projects.Count(p => p.delayedTasks > 0),
            averageProgress = withProgress.Count > 0 ? Js.Round(withProgress.Sum(p => (double)p.actualProgress!.Value) / withProgress.Count) : null,
            progressAvailable = withProgress.Count,
            scheduleCompliance = eligible.Count > 0 ? Js.Round((double)compliant / eligible.Count * 100) : null,
            complianceEligible = eligible.Count,
            compliant = compliant,
            attention = projects.Count(p => p.attentionScore is not null),
        };
    }

    /// <summary>Average progress per department (projects with a known progress only), highest first.</summary>
    public static List<DepartmentProgress> ComputeDepartmentProgress(IEnumerable<ExecutiveProject> projects)
    {
        var groups = new Dictionary<string, (string? Department, List<int> Values)>();
        foreach (var p in projects)
        {
            if (p.actualProgress is null) continue;
            var department = NullIfBlank(p.department);
            var key = department ?? " ";
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = (department, []);
            group.Values.Add(p.actualProgress.Value);
        }
        return groups.Values
            .Select(g => new DepartmentProgress
            {
                department = g.Department,
                averageProgress = Js.Round(g.Values.Sum(v => (double)v) / g.Values.Count),
                projects = g.Values.Count,
            })
            .OrderByDescending(d => d.averageProgress)
            .ThenBy(d => d.department ?? "", Comparer<string>.Create((a, b) => Arabic.Compare(a, b, CompareOptions.None)))
            .ToList();
    }

    public sealed record HistoryPoint(string ProjectId, string ReportingDate, int? ActualProgress);

    private static string LastDayOfMonth(string key)
    {
        var parts = key.Split('-');
        var y = int.Parse(parts[0]);
        var m = int.Parse(parts[1]);
        return new DateOnly(y, m, DateTime.DaysInMonth(y, m)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Monthly average of the last reported progress per project, from stored weekly updates. No invented history:
    /// fewer than two months with reports yields an empty trend.
    /// </summary>
    public static List<ProgressTrendPoint> ComputeProgressTrend(IEnumerable<HistoryPoint> history, string today, int months = 6)
    {
        var rows = history
            .Where(h => h.ReportingDate.Length == 10 && h.ActualProgress is not null && string.CompareOrdinal(h.ReportingDate, today) <= 0)
            .OrderBy(h => h.ReportingDate, StringComparer.Ordinal)
            .ToList();
        var reportMonths = rows.Select(r => r.ReportingDate[..7]).ToHashSet();
        if (reportMonths.Count < 2) return [];

        var todayParts = today.Split('-');
        var ty = int.Parse(todayParts[0]);
        var tm = int.Parse(todayParts[1]);
        var firstMonth = reportMonths.Order(StringComparer.Ordinal).First();
        var points = new List<ProgressTrendPoint>();
        for (var i = months - 1; i >= 0; i--)
        {
            var key = new DateOnly(ty, tm, 1).AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(key, firstMonth) < 0) continue;
            var monthEnd = key == today[..7] ? today : LastDayOfMonth(key);
            var latest = new Dictionary<string, int>();
            foreach (var r in rows)
            {
                if (string.CompareOrdinal(r.ReportingDate, monthEnd) > 0) break;
                latest[r.ProjectId.ToLowerInvariant()] = r.ActualProgress!.Value;
            }
            if (latest.Count == 0) continue;
            points.Add(new ProgressTrendPoint
            {
                month = key,
                averageProgress = Js.Round(latest.Values.Sum(v => (double)v) / latest.Count),
                reportedProjects = latest.Count,
            });
        }
        return points.Count >= 2 ? points : [];
    }
}
