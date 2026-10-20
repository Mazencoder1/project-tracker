using System.Globalization;
using System.Text.RegularExpressions;
using ProjectTracker.Web.Helpers;

namespace ProjectTracker.Web.Services;

/// <summary>Project schedule, work-item and colour rules (ports of lib/projectSchedule.ts, lib/workSchedule.ts, lib/phaseColor.ts).</summary>
public static partial class ProjectRules
{
    public const int MaxProjectWeeks = 520;
    public const int ProjectNameMax = 200;
    public const int ProjectDescriptionMax = 4000;
    public const int WorkItemNameMax = 200;

    [GeneratedRegex(@"^\d+$")] private static partial Regex DigitsRegex();
    [GeneratedRegex(@"^([0-9a-f]{3}|[0-9a-f]{6})$", RegexOptions.IgnoreCase)] private static partial Regex HexRegex();

    private static bool IsEmpty(object? v) => v is null || (v is string s && s.Trim().Length == 0);

    private static bool IsIsoDate(object? v) => v is string s && Js.IsIsoDate(s);

    /// <summary>Number of weeks covering start..end inclusive (rounded up), or null for invalid dates.</summary>
    public static int? CalcProjectWeeks(string startDate, string endDate)
    {
        if (!Js.IsIsoDate(startDate) || !Js.IsIsoDate(endDate)) return null;
        var start = DateOnly.ParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateOnly.ParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = end.DayNumber - start.DayNumber + 1;
        return days < 1 ? null : (int)Math.Ceiling(days / 7.0);
    }

    public sealed record ScheduleValue(string? StartDate, string? EndDate);

    public static (ScheduleValue Value, Dictionary<string, string> Errors) ValidateProjectSchedule(object? startDate, object? endDate)
    {
        var errors = new Dictionary<string, string>();
        string? start = null, end = null;

        if (!IsEmpty(startDate))
        {
            if (IsIsoDate(startDate)) start = (string)startDate!;
            else errors["start_date"] = "تاريخ البداية غير صالح — اختر التاريخ من التقويم.";
        }
        if (!IsEmpty(endDate))
        {
            if (IsIsoDate(endDate)) end = (string)endDate!;
            else errors["end_date"] = "تاريخ النهاية غير صالح — اختر التاريخ من التقويم.";
        }
        if (start is not null && end is not null && string.CompareOrdinal(end, start) < 0)
            errors["end_date"] = "تاريخ النهاية يسبق تاريخ البداية — اختر يوم البداية نفسه أو يومًا بعده.";
        else if (start is not null && end is not null && CalcProjectWeeks(start, end) > MaxProjectWeeks)
            errors["end_date"] = $"مدة المشروع يجب ألا تزيد على {MaxProjectWeeks} أسبوعًا (10 سنوات).";

        return (new ScheduleValue(start, end), errors);
    }

    /// <summary>project_code / department / priority (only the keys that were sent).</summary>
    public sealed class ProjectExtras
    {
        public bool HasProjectCode { get; set; }
        public string? ProjectCode { get; set; }
        public bool HasDepartment { get; set; }
        public string? Department { get; set; }
        public bool HasPriority { get; set; }
        public string? Priority { get; set; }
    }

    private static string? CleanExtra(object? v)
    {
        if (v is not (string or long or int or double)) return null;
        var text = (v is double d ? Js.NumberToString(d) : v.ToString())?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static (ProjectExtras Value, Dictionary<string, string> Errors) ParseProjectExtras(JsonBody body)
    {
        var value = new ProjectExtras();
        var errors = new Dictionary<string, string>();
        if (body.Has("project_code"))
        {
            var v = CleanExtra(body.Raw("project_code"));
            if (v is { Length: > 50 }) errors["project_code"] = "الحد الأقصى 50 حرفًا";
            else { value.HasProjectCode = true; value.ProjectCode = v; }
        }
        if (body.Has(Fields.Department))
        {
            var v = CleanExtra(body.Raw(Fields.Department));
            if (v is { Length: > 200 }) errors[Fields.Department] = "الحد الأقصى 200 حرف";
            else { value.HasDepartment = true; value.Department = v; }
        }
        if (body.Has("priority"))
        {
            var v = CleanExtra(body.Raw("priority"));
            if (v is not null && v is not ("high" or "medium" or "low")) errors["priority"] = "الأولوية غير صحيحة";
            else { value.HasPriority = true; value.Priority = v; }
        }
        return (value, errors);
    }

    // ── work items (milestones and tasks) ────────────────────────────────────
    private static long? Integer(object? value) => value switch
    {
        long l => l,
        int i => i,
        double d when double.IsInteger(d) => (long)d,
        string s when DigitsRegex().IsMatch(s.Trim()) && long.TryParse(s.Trim(), out var parsed) => parsed,
        _ => null,
    };

    public sealed record WorkItemValue(string Name, int WeekStart, int WeekEnd);

    public static (WorkItemValue? Value, Dictionary<string, string> Errors) ValidateWorkItem(JsonBody body, int min, int max)
    {
        var errors = new Dictionary<string, string>();
        var name = body.String("name")?.Trim() ?? "";
        var weekStart = Integer(body.Raw("week_start"));
        var weekEnd = Integer(body.Raw(Fields.WeekEnd));

        if (name.Length == 0) errors["name"] = "الاسم مطلوب.";
        else if (name.Length > WorkItemNameMax) errors["name"] = $"الاسم يجب ألا يزيد على {WorkItemNameMax} حرف.";

        if (weekStart is null) errors["week_start"] = "أسبوع البداية يجب أن يكون عددًا صحيحًا.";
        else if (weekStart < min || weekStart > max) errors["week_start"] = $"أسبوع البداية يجب أن يكون بين {min} و{max}.";

        if (weekEnd is null) errors[Fields.WeekEnd] = "أسبوع النهاية يجب أن يكون عددًا صحيحًا.";
        else if (weekEnd < min || weekEnd > max) errors[Fields.WeekEnd] = $"أسبوع النهاية يجب أن يكون بين {min} و{max}.";
        else if (weekStart is not null && weekEnd < weekStart) errors[Fields.WeekEnd] = "أسبوع النهاية يجب ألا يسبق أسبوع البداية.";

        return (errors.Count == 0 ? new WorkItemValue(name, (int)weekStart!.Value, (int)weekEnd!.Value) : null, errors);
    }

    /// <summary>Progress 0..100 as an integer, or null when invalid.</summary>
    public static int? ValidateProgress(object? value) => Integer(value) is { } n and >= 0 and <= 100 ? (int)n : null;

    // ── phase colours ────────────────────────────────────────────────────────
    public static readonly string[] PhaseColors = ["#0B213D", "#143A6C", "#8A4B08", "#9B2226", "#1B5E20"];

    private static (int R, int G, int B)? ParseHex(string color)
    {
        var m = HexRegex().Match(color.Trim().TrimStart('#'));
        if (!m.Success) return null;
        var hex = m.Groups[1].Value;
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
    }

    /// <summary>Any colour is snapped to the nearest colour of the approved palette (default: the first).</summary>
    public static string PhaseColor(string? color, int index = 0)
    {
        var fallback = PhaseColors[((index % PhaseColors.Length) + PhaseColors.Length) % PhaseColors.Length];
        if (string.IsNullOrEmpty(color)) return fallback;
        var exact = PhaseColors.FirstOrDefault(c => string.Equals(c, color.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        if (ParseHex(color) is not { } rgb) return fallback;

        var best = fallback;
        var bestDist = double.PositiveInfinity;
        foreach (var candidate in PhaseColors)
        {
            var c = ParseHex(candidate)!.Value;
            double dist = Math.Pow(c.R - rgb.R, 2) + Math.Pow(c.G - rgb.G, 2) + Math.Pow(c.B - rgb.B, 2);
            if (dist < bestDist) { bestDist = dist; best = candidate; }
        }
        return best;
    }

    // ── employee / user rules (port of lib/userRules.ts) ─────────────────────
    public const int UserNameMax = 100;
    public const int UserEmailMax = 150;
    public const int UserDepartmentMax = 100;
    public const int PasswordMin = 8;
    public const int PasswordMax = 255;
    public static readonly string[] UserRoles = ["admin", "deputy", "pm"];

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")] private static partial Regex EmailRegex();

    /// <summary>Reason a password is rejected, or null. Spaces are part of a password and are not trimmed.</summary>
    public static string? PasswordProblem(object? password)
    {
        if (password is not string s || s.Length == 0) return "كلمة المرور مطلوبة — أدخل كلمة مرور للحساب.";
        if (s.Trim().Length == 0) return "كلمة المرور لا يمكن أن تتكون من مسافات فقط — أضف حروفًا أو أرقامًا.";
        if (s.Length < PasswordMin) return $"كلمة المرور يجب ألا تقل عن {PasswordMin} محارف.";
        if (s.Length > PasswordMax) return $"كلمة المرور يجب ألا تزيد على {PasswordMax} محرفًا.";
        return null;
    }

    private static string TrimmedText(object? v) => v is string s ? s.Trim() : "";

    private static void CheckFullName(object? raw, Dictionary<string, string> value, Dictionary<string, string> errors)
    {
        var name = TrimmedText(raw);
        if (name.Length == 0) errors["full_name"] = "الاسم الكامل مطلوب — اكتب اسم الموظف كما في سجلاته.";
        else if (name.Length > UserNameMax) errors["full_name"] = $"الاسم الكامل يجب ألا يزيد على {UserNameMax} حرف.";
        else value["full_name"] = name;
    }

    private static void CheckEmail(object? raw, Dictionary<string, string> value, Dictionary<string, string> errors)
    {
        var mail = TrimmedText(raw);
        if (mail.Length == 0) errors[Fields.Email] = "البريد الإلكتروني مطلوب — يُستخدم لتسجيل دخول الموظف.";
        else if (!EmailRegex().IsMatch(mail)) errors[Fields.Email] = "صيغة البريد الإلكتروني غير صحيحة — مثال: name@example.com";
        else if (mail.Length > UserEmailMax) errors[Fields.Email] = $"البريد الإلكتروني يجب ألا يزيد على {UserEmailMax} حرفًا.";
        else value[Fields.Email] = mail;
    }

    private static void CheckDepartment(object? raw, Dictionary<string, string> value, Dictionary<string, string> errors)
    {
        var dept = TrimmedText(raw);
        if (dept.Length > UserDepartmentMax) errors[Fields.Department] = $"اسم القسم يجب ألا يزيد على {UserDepartmentMax} حرف.";
        else value[Fields.Department] = dept;
    }

    private static void CheckRole(object? raw, Dictionary<string, string> value, Dictionary<string, string> errors)
    {
        if (raw is string r && UserRoles.Contains(r)) value["role"] = r;
        else errors["role"] = "الدور غير صحيح — اختر «مدير مشروع» أو «المدير التنفيذي» أو «مدير النظام».";
    }

    /// <summary>Validated employee fields; only the keys that were validated are present.</summary>
    public sealed record UserProfileFields(
        object? FullName, object? Email, object? Department, object? Role,
        bool HasFullName, bool HasEmail, bool HasDepartment, bool HasRole);

    public static (Dictionary<string, string> Value, Dictionary<string, string> Errors) ValidateUserProfile(UserProfileFields fields, bool partial)
    {
        var (fullName, email, department, role, hasFullName, hasEmail, hasDepartment, hasRole) = fields;
        var value = new Dictionary<string, string>();
        var errors = new Dictionary<string, string>();

        if (!partial || hasFullName) CheckFullName(fullName, value, errors);
        if (!partial || hasEmail) CheckEmail(email, value, errors);
        if (hasDepartment) CheckDepartment(department, value, errors);
        if (!partial || hasRole) CheckRole(role, value, errors);
        return (value, errors);
    }
}
