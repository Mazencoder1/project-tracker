#pragma warning disable IDE1006 // Property names deliberately match the JSON contract of the original API (snake_case / camelCase).

namespace ProjectTracker.Web.ViewModels;

// Response and input shapes of the executive dashboard API (port of lib/portfolio/types.ts).
// Every property name here IS the JSON field name the existing UI reads — do not rename them.

public sealed class AppSettingsVm
{
    public int staleUpdateDays { get; set; } = 14;
    public int varianceOnTrack { get; set; } = -5;
    public int varianceAttention { get; set; } = -15;
    public int nearDeadlineDays { get; set; } = 30;

    public AppSettingsVm Clone() => (AppSettingsVm)MemberwiseClone();
}

public sealed class PersonRef
{
    public string id { get; set; } = "";
    public string full_name { get; set; } = "";
}

public sealed class MilestoneSummary
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public int week_start { get; set; }
    public int week_end { get; set; }
    public string? color { get; set; }
    public int display_order { get; set; }
    public int progress { get; set; }
    public string? start_date { get; set; }
    public string? end_date { get; set; }
    public string state { get; set; } = "not_started";
    public int delayWeeks { get; set; }
    public int? plannedProgress { get; set; }
    public int tasksCount { get; set; }
    public bool isCurrent { get; set; }
}

public sealed class PortfolioProject
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string? description { get; set; }
    public string? project_code { get; set; }
    public string? department { get; set; }
    public string? priority { get; set; }
    public string status { get; set; } = "active";
    public string? start_date { get; set; }
    public string? end_date { get; set; }
    public int? total_weeks { get; set; }
    public List<PersonRef> managers { get; set; } = [];
    public bool assignedToMe { get; set; }

    public int milestonesCount { get; set; }
    public int completedMilestones { get; set; }
    public int tasksCount { get; set; }
    public int delayedTasks { get; set; }

    public int? actualProgress { get; set; }
    public string? actualSource { get; set; }
    public int? plannedProgress { get; set; }
    public string? plannedSource { get; set; }
    public int? variance { get; set; }

    public string health { get; set; } = "unknown";
    public List<string> healthReasons { get; set; } = [];

    public bool isOverdue { get; set; }
    public int? daysToEnd { get; set; }
    public int? elapsedWeek { get; set; }
    public string? forecastEndDate { get; set; }

    public MilestoneSummary? currentMilestone { get; set; }
    public MilestoneSummary? nextMilestone { get; set; }

    public string? lastActivityAt { get; set; }
    public string? lastReportDate { get; set; }
    public bool isStale { get; set; }

    public int? openRisks { get; set; }
    public int highRisks { get; set; }
    public int criticalBlockers { get; set; }

    public string? executiveSummary { get; set; }
    public string? decisionRequired { get; set; }
    public string? decisionNeededBy { get; set; }
    public string? decisionStatus { get; set; }
    public string? supportRequired { get; set; }
    public bool needsDecision { get; set; }
    public bool needsSupport { get; set; }

    public int openActions { get; set; }
    public int overdueActions { get; set; }

    public int? attentionRank { get; set; }
    public string? attentionReason { get; set; }
}

public sealed class PortfolioKpis
{
    public int total { get; set; }
    public int onTrack { get; set; }
    public int needsAttention { get; set; }
    public int delayedOrAtRisk { get; set; }
    public int needsDecisionOrSupport { get; set; }
    public int unknown { get; set; }
    public int completed { get; set; }
    public int? averageProgress { get; set; }
}

public sealed class PortfolioResponse
{
    public string generatedAt { get; set; } = "";
    public AppSettingsVm settings { get; set; } = new();
    public PortfolioKpis kpis { get; set; } = new();
    public List<PortfolioProject> projects { get; set; } = [];
}

public sealed class ProjectUpdateVm
{
    public string id { get; set; } = "";
    public string project_id { get; set; } = "";
    public string reporting_date { get; set; } = "";
    public string? executive_summary { get; set; }
    public int? actual_progress { get; set; }
    public int? planned_progress { get; set; }
    public int? variance_percent { get; set; }
    public string health_status { get; set; } = "on_track";
    public string? current_milestone_id { get; set; }
    public string? next_milestone_id { get; set; }
    public string? forecast_end_date { get; set; }
    public string? delay_reason { get; set; }
    public string? support_required { get; set; }
    public string? support_from { get; set; }
    public string? support_impact { get; set; }
    public string? support_due_date { get; set; }
    public string? decision_required { get; set; }
    public string? decision_from { get; set; }
    public string? decision_needed_by { get; set; }
    public string? decision_status { get; set; }
    public string? updated_by { get; set; }
    public string? updated_by_name { get; set; }
    public string created_at { get; set; } = "";
}

public sealed class RiskIssueVm
{
    public string id { get; set; } = "";
    public string project_id { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? project_name { get; set; }
    public string? milestone_id { get; set; }
    public string type { get; set; } = "";
    public string title { get; set; } = "";
    public string? description { get; set; }
    public string severity { get; set; } = "";
    public string? probability { get; set; }
    public string status { get; set; } = "";
    public string? owner_id { get; set; }
    public string? owner_name { get; set; }
    public string? due_date { get; set; }
    public string? mitigation { get; set; }
    public string? created_by { get; set; }
    public string created_at { get; set; } = "";
    public string updated_at { get; set; } = "";
}

public sealed class MeetingActionVm
{
    public string id { get; set; } = "";
    public string project_id { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? project_name { get; set; }
    public string meeting_date { get; set; } = "";
    public string action_type { get; set; } = "";
    public string description { get; set; } = "";
    public string? owner_id { get; set; }
    public string? owner_name { get; set; }
    public string? supporting_department { get; set; }
    public string? due_date { get; set; }
    public string status { get; set; } = "";
    public bool isOverdue { get; set; }
    public string? decision_notes { get; set; }
    public string? closure_notes { get; set; }
    public string? created_by { get; set; }
    public string? created_by_name { get; set; }
    public string created_at { get; set; } = "";
    public string updated_at { get; set; } = "";
}

public sealed class AuditEntryVm
{
    public string id { get; set; } = "";
    public string? user_id { get; set; }
    public string? user_name { get; set; }
    public string action { get; set; } = "";
    public string entity_type { get; set; } = "";
    public string? entity_id { get; set; }
    public string? project_id { get; set; }
    public string? details { get; set; }
    public string created_at { get; set; } = "";
}

public sealed class ProjectSummaryResponse
{
    public PortfolioProject project { get; set; } = new();
    public List<MilestoneSummary> milestones { get; set; } = [];
    public ProjectUpdateVm? latestUpdate { get; set; }
    public List<ProjectUpdateVm> recentUpdates { get; set; } = [];
    public List<RiskIssueVm> risks { get; set; } = [];
    public List<MeetingActionVm> actions { get; set; } = [];
    public bool canWrite { get; set; }
    public bool isAdmin { get; set; }
    public AppSettingsVm settings { get; set; } = new();
}

/// <summary>A project with its milestones and recent updates, before the response is shaped.</summary>
public sealed record BuiltProject(PortfolioProject Project, List<MilestoneSummary> Milestones, List<ProjectUpdateVm> Updates);

public sealed class ProjectUpdateInput
{
    public string reporting_date { get; set; } = "";
    public string? executive_summary { get; set; }
    public int? actual_progress { get; set; }
    public int? planned_progress { get; set; }
    public string health_status { get; set; } = "on_track";
    public string? current_milestone_id { get; set; }
    public string? next_milestone_id { get; set; }
    public string? forecast_end_date { get; set; }
    public string? delay_reason { get; set; }
    public string? support_required { get; set; }
    public string? support_from { get; set; }
    public string? support_impact { get; set; }
    public string? support_due_date { get; set; }
    public string? decision_required { get; set; }
    public string? decision_from { get; set; }
    public string? decision_needed_by { get; set; }
    public string? decision_status { get; set; }
}
