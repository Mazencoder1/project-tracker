using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;
using ProjectTracker.Web.Services.Email;
using ProjectTracker.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTracker.Web.Controllers;

/// <summary>
/// Settings → e-mail templates. Administrator only: the policy is enforced here on the server for every action, so hiding
/// the link in the page is a convenience and never the protection (a deputy or project manager gets 403 from each call).
/// </summary>
[Authorize(Policy = TrackerAuth.AdminOnlyPolicy)]
[Route("api")]
public sealed class EmailTemplatesController(
    IEmailTemplateService templates,
    IEmailService email,
    WeeklyReminderSettingsService reminder,
    AuditService audit,
    ProjectLinkBuilder links,
    AppConfig config,
    ILogger<EmailTemplatesController> logger) : ApiControllerBase
{
    /// <summary>Everything the settings page needs in one call: the templates, the placeholder list, the weekly schedule and mail status.</summary>
    [HttpGet("email-templates")]
    public async Task<IActionResult> List()
    {
        return Json(new
        {
            templates = await templates.ListAsync(),
            variables = EmailTemplateCatalog.Variables.Select(v => new EmailVariableVm { name = v.Name, label = v.LabelAr, sample = v.Sample, isHtml = v.IsHtml }).ToList(),
            weeklyReminder = await reminder.LoadAsync(),
            mail = new { enabled = config.EmailEnabled, hasBaseUrl = links.BaseUrl() is not null },
        });
    }

    [HttpGet("email-templates/{key}")]
    public async Task<IActionResult> Get(string key)
    {
        if (!EmailTemplateKeys.IsKnown(key)) return Error(Fields.TemplateNotFound, 404);
        var template = await templates.GetAsync(key);
        return template is null ? Error(Fields.TemplateNotFound, 404) : Json(new { template });
    }

    /// <summary>Edit name, subject, body and the on/off switch. The body is validated (known placeholders only) and sanitized before it is stored.</summary>
    [HttpPut("email-templates/{key}")]
    public async Task<IActionResult> Update(string key)
    {
        if (!EmailTemplateKeys.IsKnown(key)) return Error(Fields.TemplateNotFound, 404);
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();
        if (body.Has(Fields.IsEnabled) && body.Raw(Fields.IsEnabled) is not bool) return ApiResults.ValidationFailed(new() { [Fields.IsEnabled] = "قيمة التفعيل غير صحيحة." });

        var edit = new TemplateEdit(body.String("nameAr"), body.String("subject"), body.String("messageText"), body.Raw(Fields.IsEnabled) as bool?);
        var result = await templates.UpdateAsync(key, edit, Me.Id);
        if (result.NotFound) return Error(Fields.TemplateNotFound, 404);
        if (!result.Ok) return ApiResults.ValidationFailed(result.Errors);

        await audit.WriteAsync(Me.Id, "update", "email_template", null, null, new { templateKey = key, isEnabled = result.Template!.isEnabled, subject = result.Template.subject });
        return Json(new { template = result.Template });
    }

    /// <summary>The message as the reader will see it, with sample data. Nothing is saved and no project is read or changed.</summary>
    [HttpPost("email-templates/{key}/preview")]
    public async Task<IActionResult> Preview(string key)
    {
        if (!EmailTemplateKeys.IsKnown(key)) return Error(Fields.TemplateNotFound, 404);
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        var prepared = await PrepareAsync(key, body);
        if (prepared.Error is not null) return prepared.Error;
        var rendered = templates.RenderSample(key, prepared.Content!);
        return Json(new EmailPreviewVm { subject = rendered.Subject, bodyHtml = rendered.BodyHtml });
    }

    /// <summary>Sends the template as currently edited (saved or not) to one address, with sample data, so SMTP and layout can be checked.</summary>
    [HttpPost("email-templates/{key}/test")]
    public async Task<IActionResult> SendTest(string key)
    {
        if (!EmailTemplateKeys.IsKnown(key)) return Error(Fields.TemplateNotFound, 404);
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        var to = body.String("to")?.Trim() ?? "";
        if (to.Length == 0) return ApiResults.ValidationFailed(new() { ["to"] = "أدخل البريد الإلكتروني الذي سيُرسل إليه البريد التجريبي." });
        if (!email.IsValidAddress(to)) return ApiResults.ValidationFailed(new() { ["to"] = "البريد الإلكتروني غير صالح." });

        var prepared = await PrepareAsync(key, body);
        if (prepared.Error is not null) return prepared.Error;
        var rendered = templates.RenderSample(key, prepared.Content!);

        var result = await email.SendAsync(to, rendered.Subject, rendered.BodyHtml);
        await audit.WriteAsync(Me.Id, "send_test", "email_template", null, null, new { templateKey = key, to, success = result.Success });
        if (!result.Success)
        {
            logger.LogWarning("Test e-mail for {Template} failed: {Error}", key, result.Error);
            return Json(new { error = "تعذّر إرسال البريد التجريبي. تحقق من إعدادات SMTP (Email:*) وحاول مجددًا.", detail = result.Error }, 502);
        }
        return Json(new { success = true, to });
    }

    [HttpPut("email-settings/weekly-reminder")]
    public async Task<IActionResult> SaveWeeklyReminder()
    {
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();
        var errors = new Dictionary<string, string>();
        if (body.Has(Fields.Enabled) && body.Raw(Fields.Enabled) is not bool) errors[Fields.Enabled] = "قيمة التفعيل غير صحيحة.";
        // JsonBody hands numbers back as long or double, depending on the JSON text.
        int? day = body.Raw("day") switch
        {
            long l => (int)Math.Clamp(l, -1, 99),
            double d when double.IsInteger(d) => (int)Math.Clamp(d, -1, 99),
            _ => null,
        };
        if (body.Has("day") && day is null) errors["day"] = "اليوم يجب أن يكون رقمًا بين 0 و6.";
        if (body.Has("time") && body.String("time") is null) errors["time"] = "الوقت يجب أن يكون نصًا بصيغة HH:mm.";
        if (errors.Count > 0) return ApiResults.ValidationFailed(errors);

        var saveErrors = await reminder.SaveAsync(body.Raw(Fields.Enabled) as bool?, day, body.String("time"), Me.Id);
        if (saveErrors.Count > 0) return ApiResults.ValidationFailed(saveErrors);

        var settings = await reminder.LoadAsync();
        await audit.WriteAsync(Me.Id, "update", "email_settings", null, null, settings);
        return Json(new { weeklyReminder = settings });
    }

    /// <summary>Subject/body from the request, falling back to the stored template — validated and sanitized like a save.</summary>
    private async Task<(PreparedContent? Content, IActionResult? Error)> PrepareAsync(string key, JsonBody body)
    {
        var stored = await templates.GetAsync(key);
        if (stored is null) return (null, Error(Fields.TemplateNotFound, 404));
        var prepared = templates.Prepare(key, body.Has("subject") ? body.String("subject") : stored.subject, body.Has("messageText") ? body.String("messageText") : stored.messageText);
        return prepared.Ok ? (prepared.Content, null) : (null, ApiResults.ValidationFailed(prepared.Errors));
    }
}
