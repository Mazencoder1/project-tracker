using Ganss.Xss;

namespace ProjectTracker.Web.Services.Email;

public sealed record SanitizedHtml(string Html, IReadOnlyList<string> Removed);

public interface IEmailHtmlSanitizer
{
    SanitizedHtml Sanitize(string html);
}

/// <summary>
/// Allow-list HTML sanitizer for e-mail bodies (HtmlSanitizer / AngleSharp). Scripts, event handlers, iframes,
/// forms, style blocks and javascript:/data: links never survive; only http, https and mailto links are kept.
/// It runs when a template is saved AND again on the final rendered message, so neither an edited database row nor
/// a variable value can smuggle markup into an e-mail.
/// </summary>
public sealed class EmailHtmlSanitizer : IEmailHtmlSanitizer
{
    private static readonly string[] ExtraAttributes =
        ["dir", "lang", "role", "align", "valign", "width", "height", "cellpadding", "cellspacing", "border", "bgcolor", "colspan", "rowspan"];

    private static readonly string[] ExtraCssProperties =
        ["direction", "unicode-bidi", "border-radius", "display", "line-height", "max-width", "min-width", "letter-spacing", "text-decoration",
         "white-space", "border-collapse", "padding-inline-start", "padding-inline-end", "margin-inline-start", "margin-inline-end"];

    // An explicit allow-list (not the library default, which also lets forms and media through): text, layout and tables only.
    private static readonly string[] AllowedTags =
    [
        "a", "abbr", "b", "blockquote", "br", "caption", "center", "cite", "code", "col", "colgroup", "dd", "div", "dl", "dt", "em", "font",
        "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "img", "li", "ol", "p", "pre", "s", "small", "span", "strike", "strong", "sub", "sup",
        "table", "tbody", "td", "tfoot", "th", "thead", "tr", "u", "ul",
    ];

    private static HtmlSanitizer Create()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in AllowedTags) sanitizer.AllowedTags.Add(tag);
        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" }) sanitizer.AllowedSchemes.Add(scheme);
        sanitizer.AllowDataAttributes = false;
        foreach (var attribute in ExtraAttributes) sanitizer.AllowedAttributes.Add(attribute);
        foreach (var property in ExtraCssProperties) sanitizer.AllowedCssProperties.Add(property);
        return sanitizer;
    }

    public SanitizedHtml Sanitize(string html)
    {
        var removed = new List<string>();
        var sanitizer = Create();
        sanitizer.RemovingTag += (_, e) => removed.Add($"<{e.Tag.LocalName}>");
        sanitizer.RemovingAttribute += (_, e) => removed.Add($"{e.Attribute.Name} ({e.Tag.LocalName})");
        sanitizer.RemovingStyle += (_, e) => removed.Add($"style:{e.Style.Name}");
        sanitizer.RemovingAtRule += (_, e) => removed.Add($"@{e.Rule.Type}");
        return new SanitizedHtml(sanitizer.Sanitize(html), removed.Distinct().ToList());
    }
}
