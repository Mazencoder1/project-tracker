using System.Net;
using System.Text.RegularExpressions;

namespace ProjectTracker.Web.Services.Email;

/// <summary>
/// Placeholder validation and substitution ({{Name}}). Pure functions. Substitution is a single pass, so a value that
/// itself contains "{{X}}" is never expanded again, and every plain-text value is HTML-encoded in the body so a
/// project name can never become markup.
/// </summary>
public static partial class EmailTemplateEngine
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    // Attributes whose value is a URL: only the system-built ProjectUrl may appear inside them.
    [GeneratedRegex("""\b(?:href|src|action|formaction|background|poster|xlink:href)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))""", RegexOptions.IgnoreCase)]
    private static partial Regex UrlAttributeRegex();

    [GeneratedRegex(@"[\r\n\t\u0000-\u001F\u2028\u2029]+")]
    private static partial Regex ControlCharsRegex();

    public static IReadOnlyList<string> Placeholders(string text) =>
        PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Arabic error messages for the administrator; an empty list means the text is acceptable.</summary>
    public static List<string> Validate(string text, bool isSubject)
    {
        var errors = new List<string>();

        foreach (var name in Placeholders(text))
        {
            if (!EmailTemplateCatalog.IsKnownVariable(name))
                errors.Add($"المتغير {{{{{name}}}}} غير معروف. استخدم المتغيرات المعروضة بجانب المحرر فقط.");
            else if (isSubject && EmailTemplateCatalog.IsHtmlVariable(name))
                errors.Add($"المتغير {{{{{name}}}}} لا يصلح في العنوان لأنه يحتوي على HTML.");
        }

        // Braces that are not part of a valid placeholder ("{{ Name", "{{}}", "{{Na me}}") would reach the reader as-is.
        var rest = PlaceholderRegex().Replace(text, "");
        if (rest.Contains("{{") || rest.Contains("}}"))
            errors.Add("توجد أقواس {{ }} غير صحيحة. صيغة المتغير الصحيحة: {{ProjectName}} بلا مسافات داخل الاسم.");

        if (!isSubject)
        {
            foreach (var value in UrlAttributeRegex().Matches(text).Select(FirstSuccessfulGroup))
            {
                var offenders = Placeholders(value).Where(n => n != EmailTemplateCatalog.ProjectUrlVariable).ToList();
                if (offenders.Count > 0)
                    errors.Add($"لا يُسمح بالمتغير {{{{{offenders[0]}}}}} داخل رابط (href/src). المتغير الوحيد المسموح داخل الروابط هو {{{{{EmailTemplateCatalog.ProjectUrlVariable}}}}}.");
            }
        }
        return errors.Distinct().ToList();
    }

    /// <summary>Subject is plain text: values are inserted as-is, minus line breaks and control characters (no header injection).</summary>
    public static string RenderSubject(string template, IReadOnlyDictionary<string, string?> values)
    {
        var text = PlaceholderRegex().Replace(template, m => ControlCharsRegex().Replace(Lookup(values, m.Groups[1].Value), " "));
        return ControlCharsRegex().Replace(text, " ").Trim();
    }

    /// <summary>Body is HTML: text values are encoded, and only the variables the catalogue marks as HTML are inserted raw.</summary>
    public static string RenderBody(string template, IReadOnlyDictionary<string, string?> values) =>
        PlaceholderRegex().Replace(WithoutEmptyLinkBlock(template, values), m =>
        {
            var name = m.Groups[1].Value;
            var value = Lookup(values, name);
            return EmailTemplateCatalog.IsHtmlVariable(name) ? value : WebUtility.HtmlEncode(value);
        });

    /// <summary>Drops the project-link block of the frame when no link is known, otherwise only its markers.</summary>
    private static string FirstSuccessfulGroup(Match match)
    {
        if (match.Groups[1].Success) return match.Groups[1].Value;
        return match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
    }

    private static string WithoutEmptyLinkBlock(string template, IReadOnlyDictionary<string, string?> values)
    {
        var hasLink = !string.IsNullOrWhiteSpace(Lookup(values, EmailTemplateCatalog.ProjectUrlVariable));
        var start = Regex.Escape(EmailTemplateShells.LinkStart);
        var end = Regex.Escape(EmailTemplateShells.LinkEnd);
        return hasLink
            ? template.Replace(EmailTemplateShells.LinkStart, "").Replace(EmailTemplateShells.LinkEnd, "")
            : Regex.Replace(template, start + @"[\s\S]*?" + end, "", RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    private static string Lookup(IReadOnlyDictionary<string, string?> values, string name) =>
        EmailTemplateCatalog.IsKnownVariable(name) && values.TryGetValue(name, out var v) ? v ?? "" : "";
}
