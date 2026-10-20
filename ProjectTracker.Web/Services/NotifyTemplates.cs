namespace ProjectTracker.Web.Services;

/// <summary>HTML e-mail building blocks (port of lib/notifyTemplates.ts). Every text is HTML-escaped.</summary>
public sealed class NotifyTemplates(PublicUrlResolver publicUrl)
{
    public const string OrgName = "إدارة المشاريع";
    public const string SystemName = "منصة متابعة المشاريع";

    public const string Green = "#134586";
    private const string GrayText = "#555";
    private const string Border = "#ddd";

    public static string Escape(string value) => value
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&#39;").Replace("\"", "&quot;");

    /// <summary>Public address of the system (for links inside e-mails), or null — see PublicUrlResolver.</summary>
    public string? AppBaseUrl() => publicUrl.BaseUrl();

    private string LinkButton(string path, string label)
    {
        var baseUrl = AppBaseUrl();
        if (baseUrl is null) return "";
        var href = Escape($"{baseUrl}{path}");
        return $"<p style=\"margin:24px 0 8px\"><a href=\"{href}\" style=\"background:{Green};color:#fff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold\">{Escape(label)}</a></p>";
    }

    public string Layout(string heading, string body, string? ctaPath = null, string? ctaLabel = null)
    {
        var cta = ctaPath is not null && ctaLabel is not null ? LinkButton(ctaPath, ctaLabel) : "";
        return $"""
            <div dir="rtl" style="font-family:Arial,Tahoma,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid {Border};border-radius:8px;color:#222">
            <h2 style="color:{Green};margin:0 0 4px">{Escape(heading)}</h2>
            <p style="margin:0 0 20px;color:{GrayText};font-size:13px">{Escape(SystemName)} — {Escape(OrgName)}</p>
            {body}
            {cta}
            <hr style="border:0;border-top:1px solid {Border};margin:24px 0 12px">
            <p style="color:{GrayText};font-size:12px;margin:0">رسالة آلية من {Escape(SystemName)}. لا تتضمّن أي كلمة مرور، ولا حاجة للرد عليها.</p>
            </div>
            """;
    }

    /// <summary>A table whose cells are already-escaped HTML.</summary>
    public static string Table(IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        var head = string.Concat(headers.Select(h => $"<th style=\"text-align:right;padding:8px;border-bottom:2px solid {Green};font-size:13px;white-space:nowrap\">{Escape(h)}</th>"));
        var body = string.Concat(rows.Select((cells, i) =>
            $"<tr style=\"background:{(i % 2 == 1 ? "#fafafa" : "#fff")}\">{string.Concat(cells.Select(c => $"<td style=\"padding:8px;border-bottom:1px solid #eee;font-size:13px\">{c}</td>"))}</tr>"));
        return $"<table role=\"presentation\" style=\"width:100%;border-collapse:collapse;margin:8px 0\">\n<thead><tr>{head}</tr></thead><tbody>{body}</tbody></table>";
    }

    public static string Callout(string text, string tone = "warn")
    {
        var (background, color) = tone switch
        {
            "danger" => ("#fdecea", "#a31c12"),
            "ok" => ("#e8eef5", "#0f376b"),
            _ => ("#fff8e1", "#8a6100"),
        };
        return $"<p style=\"background:{background};color:{color};padding:10px 12px;border-radius:6px;margin:12px 0;font-size:13px;font-weight:bold\">{Escape(text)}</p>";
    }

    public static string Paragraph(string text) => $"<p style=\"margin:0 0 12px;line-height:1.8\">{Escape(text)}</p>";

    public static string BulletList(IReadOnlyCollection<string> items) =>
        items.Count == 0 ? "" : $"<ul style=\"margin:0 0 12px;padding-inline-start:20px;line-height:1.9\">{string.Concat(items.Select(i => $"<li>{Escape(i)}</li>"))}</ul>";

    public static string Bold(string text) => $"<strong>{Escape(text)}</strong>";

    /// <summary>
    /// Plain account e-mail: a card with «مرحبًا {name}» followed by <paramref name="contentHtml"/> (already-escaped HTML).
    /// Used by the account and password e-mails, which are not editable templates.
    /// </summary>
    public static string GreetingCard(string fullName, string contentHtml)
    {
        var name = Escape(string.IsNullOrEmpty(fullName) ? "الموظف" : fullName);
        return $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif;max-width:600px;margin:auto;padding:24px;border:1px solid {Border};border-radius:8px\">"
            + $"<h2 style=\"color:{Green}\">مرحبًا {name}</h2>"
            + contentHtml + "</div>";
    }

    /// <summary>A centred call-to-action link button (already-escaped <paramref name="href"/>).</summary>
    public static string CenteredLinkButton(string href, string label) =>
        $"<p style=\"text-align:center\"><a href=\"{href}\" style=\"display:inline-block;background:{Green};color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:bold\">{Escape(label)}</a></p>";

    /// <summary>A small grey note at the end of an e-mail.</summary>
    public static string FinePrint(string text) => $"<p style=\"color:#666;font-size:13px\">{Escape(text)}</p>";
}
