#pragma warning disable IDE1006 // Property names deliberately match the JSON contract of the original API.

namespace ProjectTracker.Web.ViewModels;

public sealed class SupportRequest
{
    public string id { get; set; } = "";
    public string kind { get; set; } = "support";
    public string source { get; set; } = "weekly_update";
    public string description { get; set; } = "";
    public string? party { get; set; }
    public string? requestedAt { get; set; }
    public string? dueDate { get; set; }
    public string status { get; set; } = "open";
    public string? milestoneName { get; set; }
}

public sealed class ExecutiveProject
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string? description { get; set; }
    public string? project_code { get; set; }
    public string? department { get; set; }
    public string status { get; set; } = "";
    public string? start_date { get; set; }
    public string? end_date { get; set; }
    public int? total_weeks { get; set; }
    public List<string> managers { get; set; } = [];
    public string execStatus { get; set; } = "unknown";
    public int? actualProgress { get; set; }
    public int? plannedProgress { get; set; }
    public bool plannedEstimated { get; set; }
    public int? variance { get; set; }
    public int? daysToEnd { get; set; }
    public bool isOverdue { get; set; }
    public string? currentMilestoneName { get; set; }
    public int tasksCount { get; set; }
    public int delayedTasks { get; set; }
    public List<SupportRequest> supportRequests { get; set; } = [];
    public bool needsSupport { get; set; }
    public bool nearDeadline { get; set; }
    public bool? scheduleCompliant { get; set; }
    public List<string> attentionReasons { get; set; } = [];
    public int? attentionScore { get; set; }
}

public sealed class ExecutiveKpis
{
    public int total { get; set; }
    public int completed { get; set; }
    public int onTrack { get; set; }
    public int needsAttention { get; set; }
    public int delayed { get; set; }
    public int stalled { get; set; }
    public int unknown { get; set; }
    public int needsSupport { get; set; }
    public int nearDeadline { get; set; }
    public int delayedTasks { get; set; }
    public int projectsWithDelayedTasks { get; set; }
    public int? averageProgress { get; set; }
    public int progressAvailable { get; set; }
    public int? scheduleCompliance { get; set; }
    public int complianceEligible { get; set; }
    public int compliant { get; set; }
    public int attention { get; set; }
}

public sealed class DepartmentProgress
{
    public string? department { get; set; }
    public int averageProgress { get; set; }
    public int projects { get; set; }
}

public sealed class ProgressTrendPoint
{
    public string month { get; set; } = "";
    public int averageProgress { get; set; }
    public int reportedProjects { get; set; }
}

public sealed class ExecutiveDashboardResponse
{
    public string generatedAt { get; set; } = "";
    public string today { get; set; } = "";
    public AppSettingsVm settings { get; set; } = new();
    public ExecutiveKpis kpis { get; set; } = new();
    public List<ExecutiveProject> projects { get; set; } = [];
    public List<DepartmentProgress> departments { get; set; } = [];
    public List<ProgressTrendPoint> trend { get; set; } = [];
}
