using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ProjectTracker.Web.Services;

/// <summary>Dashboard settings stored in app_settings; missing or corrupt values fall back to the defaults.</summary>
public sealed class AppSettingsService(AppDbContext context, ILogger<AppSettingsService> logger)
{
    private static readonly (string Field, string Key)[] Keys =
    [
        ("staleUpdateDays", "stale_update_days"),
        ("varianceOnTrack", "variance_on_track"),
        ("varianceAttention", "variance_attention"),
        ("nearDeadlineDays", "near_deadline_days"),
    ];

    public async Task<AppSettingsVm> LoadAsync(CancellationToken ct = default)
    {
        var settings = new AppSettingsVm();
        try
        {
            var stored = await context.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, ct);
            foreach (var (field, key) in Keys)
            {
                if (!stored.TryGetValue(key, out var raw)
                    || !int.TryParse(raw, System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowTrailingWhite | System.Globalization.NumberStyles.AllowLeadingSign,
                        System.Globalization.CultureInfo.InvariantCulture, out var value)) continue;
                switch (field)
                {
                    case "staleUpdateDays": settings.staleUpdateDays = value; break;
                    case "varianceOnTrack": settings.varianceOnTrack = value; break;
                    case "varianceAttention": settings.varianceAttention = value; break;
                    case "nearDeadlineDays": settings.nearDeadlineDays = value; break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the dashboard settings; the defaults are used");
        }
        return settings;
    }

    public async Task SaveAsync(AppSettingsVm settings, string? userId, CancellationToken ct = default)
    {
        var updatedBy = Js.IsUuid(userId) ? Guid.Parse(userId!) : (Guid?)null;
        var values = new Dictionary<string, int>
        {
            ["stale_update_days"] = settings.staleUpdateDays,
            ["variance_on_track"] = settings.varianceOnTrack,
            ["variance_attention"] = settings.varianceAttention,
            ["near_deadline_days"] = settings.nearDeadlineDays,
        };
        var existing = await context.AppSettings.Where(s => values.Keys.Contains(s.SettingKey)).ToDictionaryAsync(s => s.SettingKey, ct);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var row))
            {
                row.SettingValue = value.ToString();
                row.UpdatedBy = updatedBy;
                row.UpdatedAt = DateTime.Now;
            }
            else
            {
                context.AppSettings.Add(new Models.AppSettingRow { SettingKey = key, SettingValue = value.ToString(), UpdatedBy = updatedBy });
            }
        }
        await context.SaveChangesAsync(ct);
    }
}

public sealed record PortfolioUser(string Id, string Role);

/// <summary>Executive-dashboard data assembly (port of lib/portfolio/queries.ts): one batch of queries, no N+1.</summary>
public sealed class PortfolioQueries(SqlDb db, AppSettingsService appSettings)
{
    // ── row mappers ─────────────────────────────────────────────────────────
    public static ProjectUpdateVm MapProjectUpdate(Row r) => new()
    {
        id = r.Str("id")!,
        project_id = r.Str(Fields.ProjectId)!,
        reporting_date = r.Date("reporting_date") ?? "",
        executive_summary = r.Str("executive_summary"),
        actual_progress = r.Int("actual_progress"),
        planned_progress = r.Int("planned_progress"),
        variance_percent = r.Int("variance_percent"),
        health_status = r.Str("health_status")!,
        current_milestone_id = r.Str("current_milestone_id"),
        next_milestone_id = r.Str("next_milestone_id"),
        forecast_end_date = r.Date("forecast_end_date"),
        delay_reason = r.Str("delay_reason"),
        support_required = r.Str("support_required"),
        support_from = r.Str("support_from"),
        support_impact = r.Str("support_impact"),
        support_due_date = r.Date("support_due_date"),
        decision_required = r.Str("decision_required"),
        decision_from = r.Str("decision_from"),
        decision_needed_by = r.Date("decision_needed_by"),
        decision_status = r.Str("decision_status"),
        updated_by = r.Str("updated_by"),
        updated_by_name = r.Str("updated_by_name"),
        created_at = r.Iso(Fields.CreatedAt) ?? "",
    };

    public static RiskIssueVm MapRisk(Row r) => new()
    {
        id = r.Str("id")!,
        project_id = r.Str(Fields.ProjectId)!,
        milestone_id = r.Str("milestone_id"),
        type = r.Str("type")!,
        title = r.Str("title")!,
        description = r.Str(Fields.Description),
        severity = r.Str("severity")!,
        probability = r.Str("probability"),
        status = r.Str(Fields.Status)!,
        owner_id = r.Str("owner_id"),
        owner_name = r.Str("owner_name"),
        due_date = r.Date("due_date"),
        mitigation = r.Str("mitigation"),
        created_by = r.Str("created_by"),
        created_at = r.Iso(Fields.CreatedAt) ?? "",
        updated_at = r.Iso("updated_at") ?? "",
        project_name = r.Has(Fields.ProjectName) ? r.Str(Fields.ProjectName) : null,
    };

    public static MeetingActionVm MapMeetingAction(Row r, string today)
    {
        var dueDate = r.Date("due_date");
        var status = r.Str(Fields.Status)!;
        return new MeetingActionVm
        {
            id = r.Str("id")!,
            project_id = r.Str(Fields.ProjectId)!,
            meeting_date = r.Date("meeting_date") ?? "",
            action_type = r.Str("action_type")!,
            description = r.Str(Fields.Description)!,
            owner_id = r.Str("owner_id"),
            owner_name = r.Str("owner_name"),
            supporting_department = r.Str("supporting_department"),
            due_date = dueDate,
            status = status,
            isOverdue = PortfolioCalc.IsActionOverdue(status, dueDate, today),
            decision_notes = r.Str("decision_notes"),
            closure_notes = r.Str("closure_notes"),
            created_by = r.Str("created_by"),
            created_by_name = r.Str("created_by_name"),
            created_at = r.Iso(Fields.CreatedAt) ?? "",
            updated_at = r.Iso("updated_at") ?? "",
            project_name = r.Has(Fields.ProjectName) ? r.Str(Fields.ProjectName) : null,
        };
    }

    public static AuditEntryVm MapAuditEntry(Row r) => new()
    {
        id = r.Str("id")!,
        user_id = r.Str("user_id"),
        user_name = r.Str("user_name"),
        action = r.Str("action")!,
        entity_type = r.Str("entity_type")!,
        entity_id = r.Str("entity_id"),
        project_id = r.Str(Fields.ProjectId),
        details = r.Str("details"),
        created_at = r.Iso(Fields.CreatedAt) ?? "",
    };

    public const string RiskSelect = """
        SELECT r.*, o.full_name AS owner_name, p.name AS project_name
        FROM risks_issues r
        JOIN projects p ON p.id = r.project_id
        LEFT JOIN profiles o ON o.id = r.owner_id
        """;

    public const string ActionSelect = """
        SELECT a.*, o.full_name AS owner_name, c.full_name AS created_by_name, p.name AS project_name
        FROM meeting_actions a
        JOIN projects p ON p.id = a.project_id
        LEFT JOIN profiles o ON o.id = a.owner_id
        LEFT JOIN profiles c ON c.id = a.created_by
        """;

    public const string ProjectUpdateSelect = """
        SELECT u.*, pr.full_name AS updated_by_name
        FROM project_updates u
        LEFT JOIN profiles pr ON pr.id = u.updated_by
        """;

    // ── raw data ────────────────────────────────────────────────────────────
    /// <summary>Scope: every non-cancelled project (@pid NULL) or exactly one project. A constant — no user input.</summary>
    private const string Scope = "((@pid IS NULL AND ISNULL(p.status, 'active') <> 'cancelled') OR p.id = @pid)";

    private sealed record RawData(List<Row> Projects, List<Row> Managers, List<Row> Milestones, List<Row> DelayedTasks,
        List<Row> Updates, List<Row> RiskCounts, List<Row> ActionCounts);

    private async Task<RawData> LoadRawAsync(string? projectId, string today, int updatesPerProject)
    {
        var sets = await db.QueryMultiAsync($"""
            SELECT p.id, p.name, p.description, p.project_code, p.department, p.priority, p.status,
                   p.start_date, p.end_date, p.total_weeks, p.created_at
            FROM projects p
            WHERE {Scope}
            ORDER BY p.created_at DESC;

            SELECT a.project_id, pr.id, pr.full_name
            FROM assignments a
            JOIN projects p ON p.id = a.project_id
            JOIN profiles pr ON pr.id = a.user_id
            WHERE {Scope} AND ISNULL(pr.status, 'active') <> 'deleted'
            ORDER BY pr.full_name;

            SELECT m.id, m.project_id, m.name, m.week_start, m.week_end, m.color, m.display_order,
                   ISNULL(ts.avg_progress, 0) AS progress, ISNULL(ts.tasks_count, 0) AS tasks_count, ts.last_task_update
            FROM milestones m
            JOIN projects p ON p.id = m.project_id
            LEFT JOIN (
              SELECT milestone_id,
                     SUM(CAST(progress AS FLOAT) * (CASE WHEN week_end >= week_start THEN week_end - week_start + 1 ELSE 1 END))
                       / NULLIF(SUM(CASE WHEN week_end >= week_start THEN week_end - week_start + 1 ELSE 1 END), 0) AS avg_progress,
                     COUNT(*) AS tasks_count,
                     MAX(updated_at) AS last_task_update
              FROM tasks GROUP BY milestone_id
            ) ts ON ts.milestone_id = m.id
            WHERE {Scope}
            ORDER BY m.project_id, m.week_start, ISNULL(m.display_order, 0), m.name, m.id;

            SELECT m.project_id, COUNT(*) AS delayed_tasks
            FROM tasks t
            JOIN milestones m ON m.id = t.milestone_id
            JOIN projects p ON p.id = m.project_id
            WHERE {Scope}
              AND p.start_date IS NOT NULL
              AND DATEDIFF(day, p.start_date, @today) >= 0
              AND ISNULL(t.progress, 0) < 100
              AND t.week_end < DATEDIFF(day, p.start_date, @today) / 7 + 1
            GROUP BY m.project_id;

            SELECT x.* FROM (
              SELECT u.*, pr.full_name AS updated_by_name,
                     ROW_NUMBER() OVER (PARTITION BY u.project_id ORDER BY u.reporting_date DESC, u.created_at DESC) AS rn
              FROM project_updates u
              JOIN projects p ON p.id = u.project_id
              LEFT JOIN profiles pr ON pr.id = u.updated_by
              WHERE {Scope}
            ) x
            WHERE x.rn <= @updates_limit
            ORDER BY x.project_id, x.rn;

            SELECT r.project_id,
                   COUNT(*) AS total_records,
                   SUM(CASE WHEN r.status <> 'closed' THEN 1 ELSE 0 END) AS open_risks,
                   SUM(CASE WHEN r.status <> 'closed' AND r.severity IN ('high', 'critical') THEN 1 ELSE 0 END) AS high_risks,
                   SUM(CASE WHEN r.status <> 'closed' AND r.type = 'blocker' AND r.severity = 'critical' THEN 1 ELSE 0 END) AS critical_blockers,
                   SUM(CASE WHEN r.status <> 'closed' AND r.type <> 'blocker' AND r.severity = 'critical' THEN 1 ELSE 0 END) AS open_critical_risks
            FROM risks_issues r
            JOIN projects p ON p.id = r.project_id
            WHERE {Scope}
            GROUP BY r.project_id;

            SELECT a.project_id,
                   SUM(CASE WHEN a.status IN ('new', 'in_progress') THEN 1 ELSE 0 END) AS open_actions,
                   SUM(CASE WHEN a.status IN ('new', 'in_progress') AND a.due_date < @today THEN 1 ELSE 0 END) AS overdue_actions,
                   SUM(CASE WHEN a.status IN ('new', 'in_progress') AND a.action_type = 'decision' AND a.due_date < @today THEN 1 ELSE 0 END) AS overdue_decisions
            FROM meeting_actions a
            JOIN projects p ON p.id = a.project_id
            WHERE {Scope}
            GROUP BY a.project_id;
            """,
            [SqlDb.Guid("@pid", projectId), SqlDb.Date("@today", today), SqlDb.Int("@updates_limit", updatesPerProject)]);
        return new RawData(sets[0], sets[1], sets[2], sets[3], sets[4], sets[5], sets[6]);
    }

    private static Dictionary<string, List<Row>> GroupBy(IEnumerable<Row> rows, string key = Fields.ProjectId)
    {
        var map = new Dictionary<string, List<Row>>();
        foreach (var row in rows)
        {
            var k = row.Str(key)!.ToLowerInvariant();
            if (!map.TryGetValue(k, out var list)) map[k] = list = [];
            list.Add(row);
        }
        return map;
    }

    private static Dictionary<string, Row> IndexBy(IEnumerable<Row> rows, string key = Fields.ProjectId)
    {
        var map = new Dictionary<string, Row>();
        foreach (var row in rows) map[row.Str(key)!.ToLowerInvariant()] = row;
        return map;
    }

    private static bool SameId(string? a, string? b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static (int? OpenRisks, int HighRisks, int CriticalBlockers, int OpenCriticalRisks) RiskStats(Row? risk)
    {
        int? openRisks = risk is not null && (risk.Int("total_records") ?? 0) > 0 ? risk.Int("open_risks") ?? 0 : null;
        return (openRisks, risk?.Int("high_risks") ?? 0, risk?.Int("critical_blockers") ?? 0, risk?.Int("open_critical_risks") ?? 0);
    }

    /// <summary>The latest real activity: the last weekly update or the last task update (ISO strings compare in order).</summary>
    private static string? LastActivity(List<string> taskUpdates, string? latestCreatedAt)
    {
        var activity = string.IsNullOrEmpty(latestCreatedAt) ? taskUpdates : [.. taskUpdates, latestCreatedAt];
        return activity.Count > 0 ? activity.Aggregate((a, b) => string.CompareOrdinal(b, a) > 0 ? b : a) : null;
    }

    /// <summary>Overdue decisions: those logged as meeting actions plus the weekly update's own decision when its deadline passed.</summary>
    private static int OverdueDecisions(Row? actionCounts, ProjectUpdateVm? latest, bool decisionPending, string today)
    {
        var fromReport = decisionPending && !string.IsNullOrEmpty(latest?.decision_needed_by) && string.CompareOrdinal(latest.decision_needed_by, today) < 0 ? 1 : 0;
        return (actionCounts?.Int("overdue_decisions") ?? 0) + fromReport;
    }

    /// <summary>Milestones: the raw progress (float) drives the calculations; the displayed one never reaches 100 before real completion.</summary>
    private static (List<MilestoneSummary> Milestones, List<double> RawProgress, List<string> TaskUpdates) BuildMilestones(
        IEnumerable<Row> rows, string? startDate, int? elapsedWeek)
    {
        var taskUpdates = new List<string>();
        var milestones = new List<MilestoneSummary>();
        var rawProgress = new List<double>();
        foreach (var m in rows)
        {
            var progress = m.Num("progress") ?? 0;
            var weekStart = m.Int("week_start") ?? 0;
            var weekEnd = m.Int("week_end") ?? 0;
            var st = PortfolioCalc.ComputeMilestoneState(weekStart, weekEnd, progress, elapsedWeek);
            if (m.Iso("last_task_update") is { } taskIso) taskUpdates.Add(taskIso);
            rawProgress.Add(progress);
            milestones.Add(new MilestoneSummary
            {
                id = m.Str("id")!,
                name = m.Str("name")!,
                week_start = weekStart,
                week_end = weekEnd,
                color = m.Str("color"),
                display_order = m.Int("display_order") ?? 0,
                progress = progress >= 100 ? 100 : Math.Min(99, Js.Round(progress)),
                start_date = startDate is not null ? PortfolioCalc.AddDays(startDate, (weekStart - 1) * 7) : null,
                end_date = startDate is not null ? PortfolioCalc.AddDays(startDate, weekEnd * 7 - 1) : null,
                state = st.State,
                delayWeeks = st.DelayWeeks,
                plannedProgress = st.PlannedProgress,
                tasksCount = m.Int("tasks_count") ?? 0,
                isCurrent = false,
            });
        }
        return (milestones, rawProgress, taskUpdates);
    }

    private static List<BuiltProject> BuildProjects(RawData raw, PortfolioUser user, AppSettingsVm settings, string today)
    {
        var managersBy = GroupBy(raw.Managers);
        var milestonesBy = GroupBy(raw.Milestones);
        var updatesBy = GroupBy(raw.Updates);
        var delayedBy = IndexBy(raw.DelayedTasks);
        var risksBy = IndexBy(raw.RiskCounts);
        var actionsBy = IndexBy(raw.ActionCounts);

        return raw.Projects.Select(p =>
        {
            var key = p.Str("id")!.ToLowerInvariant();
            var status = p.Str(Fields.Status) ?? "active";
            var startDate = p.Date("start_date");
            var endDate = p.Date("end_date");
            var totalWeeks = p.Int("total_weeks");
            var elapsedWeek = PortfolioCalc.ComputeElapsedWeek(startDate, today);
            var daysToEnd = PortfolioCalc.ComputeDaysToEnd(endDate, today);

            var managers = managersBy.GetValueOrDefault(key, []).Select(m => new PersonRef { id = m.Str("id")!, full_name = m.Str("full_name")! }).ToList();

            var updates = updatesBy.GetValueOrDefault(key, []).Select(MapProjectUpdate).ToList();
            var latest = updates.FirstOrDefault();
            var lastReportDate = string.IsNullOrEmpty(latest?.reporting_date) ? null : latest.reporting_date;
            var reportIsStale = PortfolioCalc.IsStaleReport(lastReportDate, today, settings.staleUpdateDays);
            var daysSinceReport = lastReportDate is not null ? PortfolioCalc.DaysBetween(lastReportDate, today) : null;

            // Milestones: the raw progress (float) drives the calculations; the displayed one never reaches 100 before real completion.
            var (milestones, rawProgress, taskUpdates) = BuildMilestones(milestonesBy.GetValueOrDefault(key, []), startDate, elapsedWeek);

            var actual = PortfolioCalc.ComputeActualProgress(
                milestones.Select((m, i) => new MilestoneProgressInput(rawProgress[i], m.tasksCount, Math.Max(1, m.week_end - m.week_start + 1))).ToList(),
                latest?.actual_progress);
            var planned = PortfolioCalc.ComputePlannedProgress(startDate, endDate, totalWeeks, today);
            var variance = PortfolioCalc.ComputeVariance(actual.Value, planned.Value);
            var isOverdue = status != "completed" && daysToEnd is < 0 && (actual.Value is null || actual.Value < 100);

            var (openRisks, highRisks, criticalBlockers, openCriticalRisks) = RiskStats(risksBy.GetValueOrDefault(key));

            var needsDecision = !string.IsNullOrEmpty(latest?.decision_required) && latest.decision_status != "taken";
            var act = actionsBy.GetValueOrDefault(key);
            var overdueDecisions = OverdueDecisions(act, latest, needsDecision, today);
            var needsSupport = !string.IsNullOrEmpty(latest?.support_required);

            var (health, reasons) = PortfolioCalc.ComputeHealth(new HealthInput(
                status, variance, isOverdue, daysToEnd, latest?.health_status, reportIsStale, daysSinceReport,
                criticalBlockers, openCriticalRisks, overdueDecisions, settings));
            var attention = PortfolioCalc.ComputeAttention(new AttentionInput(
                health, overdueDecisions, needsDecision, needsSupport, highRisks, reportIsStale, daysToEnd, actual.Value, settings));

            var (current, next) = PortfolioCalc.PickCurrentAndNext(milestones, elapsedWeek, latest?.current_milestone_id, latest?.next_milestone_id);
            foreach (var m in milestones) m.isCurrent = current is not null && m.id == current.id;

            // The latest real activity: the last weekly update or the last task update (ISO strings compare in order).
            var lastActivityAt = LastActivity(taskUpdates, latest?.created_at);

            var project = new PortfolioProject
            {
                id = p.Str("id")!,
                name = p.Str("name")!,
                description = p.Str(Fields.Description),
                project_code = p.Str("project_code"),
                department = p.Str("department"),
                priority = p.Str("priority"),
                status = status,
                start_date = startDate,
                end_date = endDate,
                total_weeks = totalWeeks,
                managers = managers,
                assignedToMe = managers.Any(m => SameId(m.id, user.Id)),

                milestonesCount = milestones.Count,
                completedMilestones = rawProgress.Count(v => v >= 100),
                tasksCount = milestones.Sum(m => m.tasksCount),
                delayedTasks = delayedBy.GetValueOrDefault(key)?.Int("delayed_tasks") ?? 0,

                actualProgress = actual.Value,
                actualSource = actual.Source,
                plannedProgress = planned.Value,
                plannedSource = planned.Source,
                variance = variance,

                health = health,
                healthReasons = reasons,

                isOverdue = isOverdue,
                daysToEnd = daysToEnd,
                elapsedWeek = elapsedWeek,
                forecastEndDate = latest?.forecast_end_date,

                currentMilestone = current,
                nextMilestone = next,

                lastActivityAt = lastActivityAt,
                lastReportDate = lastReportDate,
                isStale = reportIsStale,

                openRisks = openRisks,
                highRisks = highRisks,
                criticalBlockers = criticalBlockers,

                executiveSummary = latest?.executive_summary,
                decisionRequired = latest?.decision_required,
                decisionNeededBy = latest?.decision_needed_by,
                decisionStatus = latest?.decision_status,
                supportRequired = latest?.support_required,
                needsDecision = needsDecision,
                needsSupport = needsSupport,

                openActions = act?.Int("open_actions") ?? 0,
                overdueActions = act?.Int("overdue_actions") ?? 0,

                attentionRank = attention.Rank,
                attentionReason = attention.Reason,
            };
            return new BuiltProject(project, milestones, updates);
        }).ToList();
    }

    // ── public loaders ──────────────────────────────────────────────────────
    public async Task<PortfolioResponse> LoadPortfolioAsync(PortfolioUser user)
    {
        var today = PortfolioCalc.TodayDateOnly();
        var settings = await appSettings.LoadAsync();
        var raw = await LoadRawAsync(null, today, 1);
        var projects = PortfolioCalc.SortByAttention(BuildProjects(raw, user, settings, today).Select(b => b.Project));
        return new PortfolioResponse
        {
            generatedAt = Row.Iso(DateTime.UtcNow),
            settings = settings,
            kpis = PortfolioCalc.ComputeKpis(projects),
            projects = projects,
        };
    }

    /// <summary>All projects with their milestones in one pass — used by the notification cycle.</summary>
    public async Task<(AppSettingsVm Settings, List<BuiltProject> Projects)> LoadPortfolioWithMilestonesAsync()
    {
        var today = PortfolioCalc.TodayDateOnly();
        var settings = await appSettings.LoadAsync();
        var raw = await LoadRawAsync(null, today, 1);
        return (settings, BuildProjects(raw, new PortfolioUser("", "admin"), settings, today));
    }

    /// <summary>One project with its milestones whatever its status (cancelled included) — used by the e-mail notifications.</summary>
    public async Task<BuiltProject?> LoadBuiltProjectAsync(string projectId)
    {
        var today = PortfolioCalc.TodayDateOnly();
        var settings = await appSettings.LoadAsync();
        var raw = await LoadRawAsync(projectId, today, 1);
        return BuildProjects(raw, new PortfolioUser("", "admin"), settings, today).FirstOrDefault();
    }

    /// <summary>Names of a project's overdue tasks (same rule as the delayed-task count: ended before the current week, progress under 100).</summary>
    public async Task<List<string>> LoadOverdueTaskNamesAsync(string projectId, int max)
    {
        var rows = await db.QueryAsync(
            """
            SELECT TOP (@max) t.name
            FROM tasks t
            JOIN milestones m ON m.id = t.milestone_id
            JOIN projects p ON p.id = m.project_id
            WHERE p.id = @pid
              AND p.start_date IS NOT NULL
              AND DATEDIFF(day, p.start_date, @today) >= 0
              AND ISNULL(t.progress, 0) < 100
              AND t.week_end < DATEDIFF(day, p.start_date, @today) / 7 + 1
            ORDER BY t.week_end, t.name
            """, [SqlDb.Guid("@pid", projectId), SqlDb.Date("@today", PortfolioCalc.TodayDateOnly()), SqlDb.Int("@max", max)]);
        return rows.Select(r => r.Str("name") ?? "").Where(n => n.Length > 0).ToList();
    }

    /// <summary>One project's summary, or null when it does not exist or is cancelled.</summary>
    public async Task<ProjectSummaryResponse?> LoadProjectSummaryAsync(string projectId, PortfolioUser user)
    {
        var today = PortfolioCalc.TodayDateOnly();
        var settings = await appSettings.LoadAsync();
        var raw = await LoadRawAsync(projectId, today, 5);
        var lists = await db.QueryMultiAsync($"""
            {RiskSelect}
            WHERE r.project_id = @pid AND r.status IN ('open', 'mitigating')
            ORDER BY CASE r.severity WHEN 'critical' THEN 0 WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
                     r.created_at DESC;

            {ActionSelect}
            WHERE a.project_id = @pid AND a.status IN ('new', 'in_progress')
            ORDER BY CASE WHEN a.due_date IS NULL THEN 1 ELSE 0 END, a.due_date, a.created_at DESC;
            """, [SqlDb.Guid("@pid", projectId)]);

        var built = BuildProjects(raw, user, settings, today).FirstOrDefault();
        if (built is null) return null;
        // A cancelled project counts as "not found" in the follow-up screens until an admin reactivates it.
        if (built.Project.status == "cancelled") return null;
        return new ProjectSummaryResponse
        {
            project = built.Project,
            milestones = built.Milestones,
            latestUpdate = built.Updates.FirstOrDefault(),
            recentUpdates = built.Updates,
            risks = lists[0].Select(MapRisk).ToList(),
            actions = lists[1].Select(r => MapMeetingAction(r, today)).ToList(),
            // The deputy (role admin here means a viewer) never writes: only the assigned project manager does.
            canWrite = built.Project.assignedToMe && user.Role != "deputy",
            isAdmin = user.Role == "admin",
            settings = settings,
        };
    }

    public async Task<ExecutiveDashboardResponse> LoadExecutiveDashboardAsync(PortfolioUser user)
    {
        var today = PortfolioCalc.TodayDateOnly();
        var settings = await appSettings.LoadAsync();
        var raw = await LoadRawAsync(null, today, 1);
        var extra = await db.QueryMultiAsync("""
            SELECT a.id, a.project_id, a.action_type, a.description, a.supporting_department, a.meeting_date, a.due_date, a.status
            FROM meeting_actions a
            JOIN projects p ON p.id = a.project_id
            WHERE ISNULL(p.status, 'active') <> 'cancelled'
              AND a.status IN ('new', 'in_progress')
              AND a.action_type IN ('support', 'decision', 'escalation');

            SELECT n.id, n.milestone_id, n.content, m.project_id, m.name AS milestone_name
            FROM phase_notes n
            JOIN milestones m ON m.id = n.milestone_id
            JOIN projects p ON p.id = m.project_id
            WHERE ISNULL(p.status, 'active') <> 'cancelled' AND n.type = 'support';

            SELECT u.project_id, u.reporting_date, u.actual_progress
            FROM project_updates u
            JOIN projects p ON p.id = u.project_id
            WHERE ISNULL(p.status, 'active') <> 'cancelled'
              AND u.actual_progress IS NOT NULL
              AND u.reporting_date >= DATEADD(month, -6, @today);
            """, [SqlDb.Date("@today", today)]);
        var actionsBy = GroupBy(extra[0]);
        var notesBy = GroupBy(extra[1]);

        var built = BuildProjects(raw, user, settings, today);
        var projects = PortfolioExecutive.SortExecutiveProjects(built.Select(b =>
        {
            var openMilestones = b.Milestones.Where(m => m.progress < 100).Select(m => m.id.ToLowerInvariant()).ToHashSet();
            var key = b.Project.id.ToLowerInvariant();
            var support = PortfolioExecutive.SupportRequestsFrom(
                b.Updates.FirstOrDefault(),
                actionsBy.GetValueOrDefault(key, []).Select(a => new ActionSupportFields(
                    a.Str("id")!, a.Str("action_type")!, a.Str(Fields.Description) ?? "", a.Str("supporting_department"),
                    a.Date("meeting_date") ?? "", a.Date("due_date"), a.Str(Fields.Status)!)),
                notesBy.GetValueOrDefault(key, [])
                    .Where(n => openMilestones.Contains(n.Str("milestone_id")!.ToLowerInvariant()))
                    .Select(n => new SupportNote(n.Str("id")!, n.Str("content") ?? "", n.Str("milestone_name"))));
            return PortfolioExecutive.BuildExecutiveProject(b.Project, support, settings);
        }));

        var history = extra[2].Select(r => new PortfolioExecutive.HistoryPoint(r.Str(Fields.ProjectId)!, r.Date("reporting_date") ?? "", r.Int("actual_progress"))).ToList();

        return new ExecutiveDashboardResponse
        {
            generatedAt = Row.Iso(DateTime.UtcNow),
            today = today,
            settings = settings,
            kpis = PortfolioExecutive.ComputeExecutiveKpis(projects),
            projects = projects,
            departments = PortfolioExecutive.ComputeDepartmentProgress(projects),
            trend = PortfolioExecutive.ComputeProgressTrend(history, today),
        };
    }
}
