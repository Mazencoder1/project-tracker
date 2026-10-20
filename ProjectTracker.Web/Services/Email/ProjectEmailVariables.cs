using System.Globalization;
using System.Net;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.ViewModels;

namespace ProjectTracker.Web.Services.Email;

/// <summary>
/// Turns a project into the values the templates' placeholders stand for. This is data formatting (a status code
/// into its Arabic label, a number into "5 أيام"); the wording of the message itself is in the template row.
/// </summary>
public static class ProjectEmailVariables
{
    public const string NotAvailable = "غير متوفر";
    public const string Dash = "—";
    public const int DescriptionMax = 300;
    public const int OverdueListMax = 10;

    private static readonly Dictionary<string, string> StatusLabel = new()
    {
        ["active"] = "نشط", ["completed"] = "مكتمل", ["delayed"] = "متأخر", ["cancelled"] = "ملغى",
    };

    public static string StatusText(string? status) => StatusLabel.GetValueOrDefault(status ?? "active", status ?? "active");

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string ShortDescription(string? description)
    {
        var text = description?.Trim();
        if (string.IsNullOrEmpty(text)) return Dash;
        return text.Length <= DescriptionMax ? text : text[..DescriptionMax].TrimEnd() + "…";
    }

    /// <summary>"5 أيام" for a future end date, "متأخر 3 أيام" once it has passed, "غير متوفر" without an end date.</summary>
    public static string DaysRemaining(int? daysToEnd) => daysToEnd switch
    {
        null => NotAvailable,
        >= 0 => ArabicCount.Format(daysToEnd.Value, "day"),
        _ => $"متأخر {ArabicCount.Format(-daysToEnd.Value, "day")}",
    };

    /// <summary>Start to completion when both are known (inclusive of both days), otherwise the planned number of weeks.</summary>
    public static string Duration(string? startDate, string? completedDate, int? totalWeeks)
    {
        if (startDate is not null && completedDate is not null && PortfolioCalc.DaysBetween(startDate, completedDate) is { } days && days >= 0)
            return ArabicCount.Format(days + 1, "day");
        return totalWeeks is > 0 ? ArabicCount.Format(totalWeeks.Value, "week") : NotAvailable;
    }

    /// <summary>The overdue tasks as a ready HTML list — the only value a template receives as markup, so every name is encoded here.</summary>
    public static string OverdueList(IReadOnlyList<string> names) =>
        names.Count == 0
            ? ""
            : "<ul style=\"margin:0 0 12px;padding-inline-start:20px;line-height:1.9\">"
              + string.Concat(names.Take(OverdueListMax).Select(n => $"<li>{WebUtility.HtmlEncode(n)}</li>"))
              + "</ul>";

    public static IReadOnlyDictionary<string, string?> Build(
        PortfolioProject project, string userName, string? projectUrl, string? completedDate = null,
        int overdueCount = 0, IReadOnlyList<string>? overdueNames = null) => new Dictionary<string, string?>
    {
        ["UserName"] = Or(userName, Dash),
        ["ProjectName"] = project.name,
        ["ProjectDescription"] = ShortDescription(project.description),
        ["ProjectStatus"] = StatusText(project.status),
        ["ProjectProgress"] = project.actualProgress is { } progress ? progress.ToString(CultureInfo.InvariantCulture) + "%" : NotAvailable,
        ["ProjectStartDate"] = Or(project.start_date, Dash),
        ["ProjectEndDate"] = Or(project.end_date, Dash),
        ["ProjectCompletedDate"] = Or(completedDate, Dash),
        ["ProjectDuration"] = Duration(project.start_date, completedDate, project.total_weeks),
        ["DaysRemaining"] = DaysRemaining(project.daysToEnd),
        ["DepartmentName"] = Or(project.department, Dash),
        ["ProjectManagerName"] = project.managers.Count == 0 ? Dash : string.Join("، ", project.managers.Select(m => m.full_name)),
        ["OverdueTasksCount"] = overdueCount.ToString(CultureInfo.InvariantCulture),
        ["OverdueTasksList"] = OverdueList(overdueNames ?? []),
        [EmailTemplateCatalog.ProjectUrlVariable] = projectUrl ?? "",
    };
}
