namespace ProjectTracker.Web.Services.Email;

/// <summary>The fixed template keys. Business code knows only these keys and the values it passes to them.</summary>
public static class EmailTemplateKeys
{
    public const string ProjectAssigned = "ProjectAssigned";
    public const string ProjectWeeklyReminder = "ProjectWeeklyReminder";
    public const string ProjectCompleted = "ProjectCompleted";

    public static readonly IReadOnlyList<string> All = [ProjectAssigned, ProjectWeeklyReminder, ProjectCompleted];

    public static bool IsKnown(string? key) => key is not null && All.Contains(key, StringComparer.Ordinal);
}

/// <summary>A placeholder the editor offers: {{Name}}, what it means, and a sample used only by the preview.</summary>
public sealed record EmailVariable(string Name, string LabelAr, string Sample, bool IsHtml = false);

/// <summary>
/// Which placeholders exist and what each means. This is metadata for the editor and the validation, not message
/// content: subjects and bodies live in the EmailTemplates table.
/// </summary>
public static class EmailTemplateCatalog
{
    public const string ProjectUrlVariable = "ProjectUrl";

    public static readonly IReadOnlyList<EmailVariable> Variables =
    [
        new("UserName", "اسم المستلم", "أحمد محمد"),
        new("ProjectName", "اسم المشروع", "مشروع تجريبي — تطوير بوابة الخدمات"),
        new("ProjectDescription", "وصف مختصر للمشروع", "وصف تجريبي مختصر يوضّح هدف المشروع ونطاقه."),
        new("ProjectStatus", "حالة المشروع", "نشط"),
        new("ProjectProgress", "نسبة الإنجاز", "45%"),
        new("ProjectStartDate", "تاريخ بداية المشروع", "2026-01-11"),
        new("ProjectEndDate", "تاريخ نهاية المشروع", "2026-12-31"),
        new("ProjectCompletedDate", "تاريخ إكمال المشروع", "2026-12-20"),
        new("ProjectDuration", "مدة المشروع", "354 يومًا"),
        new("DaysRemaining", "الأيام المتبقية على نهاية المشروع", "99 يومًا"),
        new("DepartmentName", "الإدارة المالكة للمشروع", "إدارة تقنية المعلومات"),
        new("ProjectManagerName", "مدير المشروع / المسؤول عنه", "أحمد محمد، سارة علي"),
        new("OverdueTasksCount", "عدد المهام المتأخرة", "2"),
        new("OverdueTasksList", "قائمة المهام المتأخرة (HTML جاهز)", "<ul><li>مهمة تجريبية متأخرة 1</li><li>مهمة تجريبية متأخرة 2</li></ul>", IsHtml: true),
        new(ProjectUrlVariable, "الرابط المباشر لفتح المشروع في النظام", "https://system.example.com/meeting?project=00000000-0000-0000-0000-000000000000"),
    ];

    private static readonly Dictionary<string, EmailVariable> ByName = Variables.ToDictionary(v => v.Name, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> WhenSent = new()
    {
        [EmailTemplateKeys.ProjectAssigned] = "تُرسل فور إسناد مشروع إلى مستخدم (مرة واحدة لكل إسناد).",
        [EmailTemplateKeys.ProjectWeeklyReminder] = "تُرسل أسبوعيًا للمسؤول عن كل مشروع لم ينتهِ، في اليوم والوقت المحدَّدين أدناه.",
        [EmailTemplateKeys.ProjectCompleted] = "تُرسل مرة واحدة عند تحويل حالة المشروع إلى «مكتمل».",
    };

    public static bool IsKnownVariable(string name) => ByName.ContainsKey(name);

    public static bool IsHtmlVariable(string name) => ByName.TryGetValue(name, out var v) && v.IsHtml;

    public static string WhenSentAr(string templateKey) => WhenSent.GetValueOrDefault(templateKey, "");

    /// <summary>Sample values for the preview and the test e-mail. Nothing here touches project data.</summary>
    public static IReadOnlyDictionary<string, string?> SampleValues(string? sampleProjectUrl)
    {
        var values = Variables.ToDictionary(v => v.Name, v => (string?)v.Sample, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(sampleProjectUrl)) values[ProjectUrlVariable] = sampleProjectUrl;
        return values;
    }
}
