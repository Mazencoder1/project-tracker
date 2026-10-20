using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ProjectTracker.Web.Services.Email;

public sealed record TemplateEdit(string? NameAr, string? Subject, string? MessageText, bool? IsEnabled);

/// <summary>Validated subject and free-text message, ready to be saved, previewed or sent (still needs the template key to render).</summary>
public sealed record PreparedContent(string Subject, string MessageText);

/// <summary>Either the prepared content or per-field Arabic errors for the editor.</summary>
public sealed record PrepareResult(PreparedContent? Content, Dictionary<string, string> Errors)
{
    public bool Ok => Content is not null;
}

public sealed record TemplateSaveResult(EmailTemplateVm? Template, Dictionary<string, string> Errors, bool NotFound)
{
    public bool Ok => Template is not null;
}

public sealed record StoredTemplate(string Key, string Subject, string MessageText, bool IsEnabled);

public sealed record RenderedEmail(string Subject, string BodyHtml);

public interface IEmailTemplateService
{
    Task<List<EmailTemplateVm>> ListAsync(CancellationToken ct = default);
    Task<EmailTemplateVm?> GetAsync(string key, CancellationToken ct = default);
    Task<TemplateSaveResult> UpdateAsync(string key, TemplateEdit edit, string? userId, CancellationToken ct = default);

    /// <summary>Validates a subject/message-text pair for the given template (the same rules the save uses).</summary>
    PrepareResult Prepare(string templateKey, string? subject, string? messageText);

    /// <summary>Renders the given (already prepared) content with sample data only; touches no project.</summary>
    RenderedEmail RenderSample(string templateKey, PreparedContent content);

    /// <summary>The stored template for a real send, or null when the row does not exist.</summary>
    Task<StoredTemplate?> GetForSendAsync(string key, CancellationToken ct = default);

    /// <summary>Real values in, final message out: the free text dropped into the fixed frame, placeholders replaced, then sanitized.</summary>
    RenderedEmail Render(StoredTemplate template, IReadOnlyDictionary<string, string?> values);
}

/// <summary>
/// Reads and edits the EmailTemplates table and renders it. The administrator edits only NameAr, Subject,
/// MessageText (plain text) and the on/off switch — no HTML ever reaches or leaves this editor. The fixed
/// HTML frame each message is rendered into lives in code (EmailTemplateShells), not in the database.
/// </summary>
public sealed class EmailTemplateService(AppDbContext context, IEmailHtmlSanitizer sanitizer, ProjectLinkBuilder links) : IEmailTemplateService
{
    public const int SubjectMax = 300;
    public const int NameMax = 200;
    public const int MessageMax = 4_000;

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await context.Profiles.AsNoTracking().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
    }

    private static EmailTemplateVm ToVm(Models.EmailTemplate t, Dictionary<Guid, string> names) => new()
    {
        templateKey = t.TemplateKey,
        nameAr = t.NameAr,
        subject = t.Subject,
        messageText = t.MessageText,
        isEnabled = t.IsEnabled,
        whenSent = EmailTemplateCatalog.WhenSentAr(t.TemplateKey),
        updatedAt = Row.Iso(DateTime.SpecifyKind(t.UpdatedAt, DateTimeKind.Utc)),
        updatedByName = t.UpdatedBy is { } by && names.TryGetValue(by, out var name) ? name : null,
    };

    public async Task<List<EmailTemplateVm>> ListAsync(CancellationToken ct = default)
    {
        var rows = await context.EmailTemplates.AsNoTracking().ToListAsync(ct);
        var names = await UserNamesAsync(rows.Where(r => r.UpdatedBy.HasValue).Select(r => r.UpdatedBy!.Value), ct);
        // The three known keys first, in a fixed order; anything else a DBA might add follows.
        return rows.OrderBy(r => { var i = EmailTemplateKeys.All.ToList().IndexOf(r.TemplateKey); return i < 0 ? int.MaxValue : i; })
            .ThenBy(r => r.TemplateKey).Select(r => ToVm(r, names)).ToList();
    }

    public async Task<EmailTemplateVm?> GetAsync(string key, CancellationToken ct = default)
    {
        var row = await context.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateKey == key, ct);
        if (row is null) return null;
        return ToVm(row, await UserNamesAsync(row.UpdatedBy is { } by ? [by] : [], ct));
    }

    public PrepareResult Prepare(string templateKey, string? subject, string? messageText)
    {
        var errors = new Dictionary<string, string>();
        var cleanSubject = (subject ?? "").Trim();
        var message = (messageText ?? "").Trim();

        if (cleanSubject.Length == 0) errors["subject"] = "عنوان الرسالة مطلوب.";
        else if (cleanSubject.Length > SubjectMax) errors["subject"] = $"عنوان الرسالة يجب ألا يزيد على {SubjectMax} حرفًا.";
        else if (cleanSubject.Contains('<') || cleanSubject.Contains('>')) errors["subject"] = "عنوان الرسالة نص عادي ولا يقبل وسوم HTML.";
        else if (cleanSubject.Any(char.IsControl)) errors["subject"] = "عنوان الرسالة يجب أن يكون في سطر واحد.";
        else if (EmailTemplateEngine.Validate(cleanSubject, isSubject: true) is { Count: > 0 } subjectErrors) errors["subject"] = string.Join(" ", subjectErrors);

        if (message.Length == 0) errors["messageText"] = "نص الرسالة مطلوب.";
        else if (message.Length > MessageMax) errors["messageText"] = $"نص الرسالة يجب ألا يزيد على {MessageMax} حرف.";
        else if (EmailTemplateEngine.Validate(message, isSubject: false) is { Count: > 0 } messageErrors) errors["messageText"] = string.Join(" ", messageErrors);

        if (errors.Count > 0) return new PrepareResult(null, errors);
        return new PrepareResult(new PreparedContent(cleanSubject, message), errors);
    }

    public async Task<TemplateSaveResult> UpdateAsync(string key, TemplateEdit edit, string? userId, CancellationToken ct = default)
    {
        var row = await context.EmailTemplates.FirstOrDefaultAsync(t => t.TemplateKey == key, ct);
        if (row is null) return new TemplateSaveResult(null, [], NotFound: true);

        var nameAr = (edit.NameAr ?? row.NameAr).Trim();
        var prepared = Prepare(key, edit.Subject ?? row.Subject, edit.MessageText ?? row.MessageText);
        var errors = new Dictionary<string, string>(prepared.Errors);
        if (nameAr.Length == 0) errors["nameAr"] = "اسم القالب مطلوب.";
        else if (nameAr.Length > NameMax) errors["nameAr"] = $"اسم القالب يجب ألا يزيد على {NameMax} حرفًا.";
        if (errors.Count > 0) return new TemplateSaveResult(null, errors, NotFound: false);

        row.NameAr = nameAr;
        row.Subject = prepared.Content!.Subject;
        row.MessageText = prepared.Content.MessageText;
        if (edit.IsEnabled is { } enabled) row.IsEnabled = enabled;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = Js.IsUuid(userId) ? Guid.Parse(userId!) : null;
        await context.SaveChangesAsync(ct);

        return new TemplateSaveResult(await GetAsync(key, ct), [], NotFound: false);
    }

    public RenderedEmail RenderSample(string templateKey, PreparedContent content)
    {
        var values = EmailTemplateCatalog.SampleValues(links.SampleProjectUrl());
        var frame = EmailTemplateShells.Build(templateKey, content.MessageText);
        return new RenderedEmail(
            EmailTemplateEngine.RenderSubject(content.Subject, values),
            sanitizer.Sanitize(EmailTemplateEngine.RenderBody(frame, values)).Html);
    }

    public async Task<StoredTemplate?> GetForSendAsync(string key, CancellationToken ct = default)
    {
        var row = await context.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateKey == key, ct);
        return row is null ? null : new StoredTemplate(row.TemplateKey, row.Subject, row.MessageText, row.IsEnabled);
    }

    public RenderedEmail Render(StoredTemplate template, IReadOnlyDictionary<string, string?> values)
    {
        var frame = EmailTemplateShells.Build(template.Key, template.MessageText);
        return new(
            EmailTemplateEngine.RenderSubject(template.Subject, values),
            sanitizer.Sanitize(EmailTemplateEngine.RenderBody(frame, values)).Html);
    }
}
