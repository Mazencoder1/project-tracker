namespace ProjectTracker.Web.Services;

/// <summary>إدارة واحدة في قائمة «الإدارة المالكة».</summary>
public sealed record Department(string Id, string Name);

/// <summary>
/// قائمة الإدارات لحقل «الإدارة المالكة» في المشاريع. مصدرها الإعدادات (Departments:List في appsettings.json،
/// أو متغير البيئة DEPARTMENTS مفصولًا بفواصل)، وإن غابت فقائمة افتراضية عامة.
/// </summary>
public sealed class DepartmentService(AppConfig config)
{
    public IReadOnlyList<Department> GetDepartments() => config.GetDepartments().Select(n => new Department(n, n)).ToList();
}
