using System.Text.RegularExpressions;

namespace ProjectTracker.Web.Services;

/// <summary>Employee data entered by the administrator when an account is created.</summary>
public sealed record EmployeeRecord(
    string EmployeeNumber, string FullName, string JobTitle, string Department, string MainDepartment, string Section,
    string Email, string MobileNo, string Extension, string ManagerName, string ManagerNumber,
    string AuthorizedManagerName, string AuthorizedManagerNumber);

/// <summary>Employee-number rules shared by user creation and editing.</summary>
public static partial class EmployeeNumbers
{
    [GeneratedRegex(@"^\d{1,20}$")] private static partial Regex NumberRegex();

    public static string Normalize(string? value) => value is null ? "" : Helpers.Js.NormalizeDigits(value.Trim());

    public static string? EmployeeNumberProblem(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0) return "الرقم الوظيفي مطلوب.";
        if (!NumberRegex().IsMatch(normalized)) return "الرقم الوظيفي يجب أن يتكون من أرقام فقط وبحد أقصى 20 رقمًا.";
        return null;
    }
}
