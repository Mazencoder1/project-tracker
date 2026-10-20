using System.Globalization;
using System.Text.RegularExpressions;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.ViewModels;

namespace ProjectTracker.Web.Services;

public sealed class ValidationResult<T>
{
    public bool Ok { get; init; }
    public T Value { get; init; } = default!;
    public Dictionary<string, string> Errors { get; init; } = [];
}

/// <summary>
/// Input validation for the executive dashboard (port of lib/portfolio/validation.ts). Values are read as
/// loosely typed JSON values (string / number / bool / null), so the accepted and rejected inputs are the
/// same ones the original rules accepted and rejected. Messages are the original Arabic texts, per field.
/// </summary>
public static partial class PortfolioValidation
{
    private static readonly string[] ReportedHealth = ["on_track", "needs_attention", "delayed", "at_risk", "stopped"];
    private static readonly string[] DecisionStatus = ["new", "under_review", "taken"];
    private static readonly string[] RiskType = ["risk", "issue", "blocker"];
    private static readonly string[] RiskSeverity = ["low", "medium", "high", "critical"];
    private static readonly string[] RiskProbability = ["low", "medium", "high"];
    private static readonly string[] RiskStatus = ["open", "mitigating", "closed"];
    private static readonly string[] ActionType = ["decision", "support", "escalation", "corrective"];
    private static readonly string[] ActionStatus = ["new", "in_progress", "completed", "cancelled"];

    [GeneratedRegex(@"^(\d{4})-(\d{2})-(\d{2})$")] private static partial Regex DateRegex();
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T")] private static partial Regex IsoPrefix();
    [GeneratedRegex(@"^-?\d+$")] private static partial Regex IntRegex();

    /// <summary>Three states like the original: valid value, valid empty (null), or invalid (undefined).</summary>
    private readonly record struct Opt<T>(bool Valid, T? Value);

    private static Dictionary<string, object?> AsObject(Dictionary<string, object?>? input) => input ?? [];

    private static string? Text(object? v)
    {
        var s = v switch
        {
            string str => str,
            long l => l.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            double d => Js.NumberToString(d),
            _ => null,
        };
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static Opt<string?> DateValue(object? v)
    {
        var s = Text(v);
        if (s is null) return new(true, null);
        var candidate = s.Length > 10 && IsoPrefix().IsMatch(s) ? s[..10] : s;
        var m = DateRegex().Match(candidate);
        if (!m.Success) return new(false, null);
        return Js.IsIsoDate(candidate) ? new(true, candidate) : new(false, null);
    }

    private static Opt<int?> IntValue(object? v)
    {
        switch (v)
        {
            case null: return new(true, null);
            case string str:
                var s = str.Trim();
                if (s.Length == 0) return new(true, null);
                if (!IntRegex().IsMatch(s)) return new(false, null);
                return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) && Math.Abs(parsed) <= int.MaxValue
                    ? new(true, (int)parsed)
                    : new(false, null);
            case long l: return Math.Abs(l) <= int.MaxValue ? new(true, (int)l) : new(false, null);
            case int i: return new(true, i);
            case double d when double.IsInteger(d) && Math.Abs(d) <= int.MaxValue: return new(true, (int)d);
            default: return new(false, null);
        }
    }

    private static Opt<string?> EnumValue(object? v, string[] allowed)
    {
        var s = Text(v);
        if (s is null) return new(true, null);
        return allowed.Contains(s) ? new(true, s) : new(false, null);
    }

    private static Opt<string?> UuidValue(object? v)
    {
        var s = Text(v);
        if (s is null) return new(true, null);
        return Js.IsUuid(s) ? new(true, s) : new(false, null);
    }

    private static ValidationResult<T> Result<T>(T value, Dictionary<string, string> errors) =>
        new() { Ok = errors.Count == 0, Value = value, Errors = errors };

    private static class Msg
    {
        public const string Required = "هذا الحقل مطلوب";
        public const string Date = "تاريخ غير صالح";
        public const string Uuid = "معرّف غير صالح";
        public const string Choice = "قيمة غير مسموح بها";
        public const string Percent = "يجب أن تكون نسبة صحيحة بين 0 و100";
        public const string Integer = "يجب أن يكون عددًا صحيحًا";
    }

    private static void MaxLen(Dictionary<string, string> errors, string field, string? value, int max)
    {
        if (value is not null && value.Length > max) errors[field] = $"الحد الأقصى {max} حرف";
    }

    // ── weekly update ───────────────────────────────────────────────────────
    private static string? ValidateReportingDate(object? raw, string today, Dictionary<string, string> errors)
    {
        var reporting = DateValue(raw);
        if (reporting is { Valid: true, Value: null }) errors[Fields.ReportingDate] = "تاريخ التقرير مطلوب";
        else if (!reporting.Valid) errors[Fields.ReportingDate] = Msg.Date;
        else if (string.CompareOrdinal(reporting.Value, today) > 0) errors[Fields.ReportingDate] = "لا يمكن أن يكون تاريخ التقرير بعد اليوم";
        return reporting.Value;
    }

    /// <summary>The decision deadline and status that go with a requested decision.</summary>
    private static (string? NeededBy, string? Status) ValidateDecision(string? decisionRequired, object? neededByRaw, object? statusRaw, Dictionary<string, string> errors)
    {
        var neededBy = DateValue(neededByRaw);
        if (!neededBy.Valid) errors["decision_needed_by"] = Msg.Date;
        else if (decisionRequired is not null && neededBy.Value is null) errors["decision_needed_by"] = "حدد موعد القرار المطلوب";

        var statusOpt = EnumValue(statusRaw, DecisionStatus);
        var status = statusOpt.Value;
        if (!statusOpt.Valid)
        {
            errors["decision_status"] = Msg.Choice;
            status = null;
        }
        if (decisionRequired is not null && status is null) status = "new";
        return (neededBy.Valid ? neededBy.Value : null, status);
    }

    public static ValidationResult<ProjectUpdateInput> ValidateProjectUpdate(Dictionary<string, object?>? input, string today)
    {
        var o = AsObject(input);
        object? Get(string k) => o.GetValueOrDefault(k);
        var errors = new Dictionary<string, string>();

        var reportingDate = ValidateReportingDate(Get(Fields.ReportingDate), today, errors);

        int? Percent(string field)
        {
            var v = IntValue(Get(field));
            if (!v.Valid || (v.Value is { } n && (n < 0 || n > 100)))
            {
                errors[field] = Msg.Percent;
                return null;
            }
            return v.Value;
        }
        var actual = Percent("actual_progress");
        var planned = Percent("planned_progress");

        var health = EnumValue(Get("health_status"), ReportedHealth);
        if (health is { Valid: true, Value: null }) errors["health_status"] = "حالة الصحة مطلوبة";
        else if (!health.Valid) errors["health_status"] = Msg.Choice;

        var delayReason = Text(Get("delay_reason"));
        if (health.Value is "delayed" or "at_risk" or "stopped" && delayReason is null)
            errors["delay_reason"] = "سبب التأخير أو التعثر مطلوب لهذه الحالة";

        var summary = Text(Get("executive_summary"));
        MaxLen(errors, "executive_summary", summary, 2000);

        string? OptionalDate(string field)
        {
            var v = DateValue(Get(field));
            if (!v.Valid)
            {
                errors[field] = Msg.Date;
                return null;
            }
            return v.Value;
        }
        var forecast = OptionalDate("forecast_end_date");
        var supportDue = OptionalDate("support_due_date");

        var decisionRequired = Text(Get("decision_required"));
        var (decisionBy, decisionStatus) = ValidateDecision(decisionRequired, Get("decision_needed_by"), Get("decision_status"), errors);

        string? Milestone(string field)
        {
            var v = UuidValue(Get(field));
            if (!v.Valid)
            {
                errors[field] = Msg.Uuid;
                return null;
            }
            return v.Value;
        }
        var currentMilestone = Milestone("current_milestone_id");
        var nextMilestone = Milestone("next_milestone_id");

        var supportFrom = Text(Get("support_from"));
        var decisionFrom = Text(Get("decision_from"));
        MaxLen(errors, "support_from", supportFrom, 200);
        MaxLen(errors, "decision_from", decisionFrom, 200);

        return Result(new ProjectUpdateInput
        {
            reporting_date = reportingDate ?? "",
            executive_summary = summary,
            actual_progress = actual,
            planned_progress = planned,
            health_status = health.Value ?? "on_track",
            current_milestone_id = currentMilestone,
            next_milestone_id = nextMilestone,
            forecast_end_date = forecast,
            delay_reason = delayReason,
            support_required = Text(Get("support_required")),
            support_from = supportFrom,
            support_impact = Text(Get("support_impact")),
            support_due_date = supportDue,
            decision_required = decisionRequired,
            decision_from = decisionFrom,
            decision_needed_by = decisionBy,
            decision_status = decisionStatus,
        }, errors);
    }

    /// <summary>Per-field checks shared by the risk and meeting-action validators ("partial" updates only check the keys that were sent).</summary>
    private sealed class FieldChecker(Dictionary<string, object?> input, bool partial)
    {
        public Dictionary<string, string> Errors { get; } = [];
        public Dictionary<string, object?> Value { get; } = [];

        public bool Has(string key) => !partial || input.ContainsKey(key);
        public object? Get(string key) => input.GetValueOrDefault(key);

        public void Date(string key, string? requiredMessage = null)
        {
            if (!Has(key)) return;
            var v = DateValue(Get(key));
            if (requiredMessage is not null && v is { Valid: true, Value: null }) Errors[key] = requiredMessage;
            else if (!v.Valid) Errors[key] = Msg.Date;
            else Value[key] = v.Value;
        }

        public void Choice(string key, string[] allowed, string? requiredMessage = null)
        {
            if (!Has(key)) return;
            var v = EnumValue(Get(key), allowed);
            if (requiredMessage is not null && v is { Valid: true, Value: null }) Errors[key] = requiredMessage;
            else if (!v.Valid) Errors[key] = Msg.Choice;
            else Value[key] = v.Value;
        }

        /// <summary>A status column: empty means the default on create and "required" on a partial update.</summary>
        public void Status(string[] allowed, string defaultValue)
        {
            if (!Has(Fields.Status)) return;
            var v = EnumValue(Get(Fields.Status), allowed);
            if (!v.Valid) Errors[Fields.Status] = Msg.Choice;
            else if (v.Value is null)
            {
                if (partial) Errors[Fields.Status] = "الحالة مطلوبة";
                else Value[Fields.Status] = defaultValue;
            }
            else Value[Fields.Status] = v.Value;
        }

        public void Uuid(string key)
        {
            if (!Has(key)) return;
            var v = UuidValue(Get(key));
            if (!v.Valid) Errors[key] = Msg.Uuid;
            else Value[key] = v.Value;
        }

        /// <summary>Required free text with a minimum and maximum length.</summary>
        public void BoundedText(string key, string requiredMessage, string shortMessage, int min, int max)
        {
            if (!Has(key)) return;
            var v = Text(Get(key));
            if (v is null) Errors[key] = requiredMessage;
            else if (v.Length < min) Errors[key] = shortMessage;
            else if (v.Length > max) Errors[key] = $"الحد الأقصى {max} حرف";
            else Value[key] = v;
        }

        public void OptionalText(string key)
        {
            if (Has(key)) Value[key] = Text(Get(key));
        }
    }

    // ── risks, issues and blockers ──────────────────────────────────────────
    /// <summary>The validated values, keyed by column name (only the keys that were checked, in check order).</summary>
    public static ValidationResult<Dictionary<string, object?>> ValidateRisk(Dictionary<string, object?>? input, bool partial = false)
    {
        var f = new FieldChecker(AsObject(input), partial);
        f.Choice("type", RiskType, "النوع مطلوب");
        f.BoundedText(Fields.Title, "العنوان مطلوب", "العنوان قصير جدًا (3 أحرف على الأقل)", 3, 300);
        f.Choice(Fields.Severity, RiskSeverity, "الخطورة مطلوبة");
        f.Choice(Fields.Probability, RiskProbability);
        f.Status(RiskStatus, "open");
        f.Date(Fields.DueDate);
        f.Uuid(Fields.OwnerId);
        f.Uuid("milestone_id");
        f.OptionalText(Fields.Description);
        f.OptionalText("mitigation");
        return Result(f.Value, f.Errors);
    }

    // ── meeting decisions and actions ───────────────────────────────────────
    public static ValidationResult<Dictionary<string, object?>> ValidateMeetingAction(Dictionary<string, object?>? input, bool partial = false)
    {
        var f = new FieldChecker(AsObject(input), partial);
        f.Date(Fields.MeetingDate, "تاريخ الاجتماع مطلوب");
        f.Choice(Fields.ActionType, ActionType, "نوع الإجراء مطلوب");
        f.BoundedText(Fields.Description, "الوصف مطلوب", "الوصف قصير جدًا (3 أحرف على الأقل)", 3, 4000);
        f.Status(ActionStatus, "new");
        f.Date(Fields.DueDate);
        f.Uuid(Fields.OwnerId);
        if (f.Has(Fields.SupportingDepartment))
        {
            var department = Text(f.Get(Fields.SupportingDepartment));
            MaxLen(f.Errors, Fields.SupportingDepartment, department, 200);
            f.Value[Fields.SupportingDepartment] = department;
        }
        f.OptionalText("decision_notes");
        f.OptionalText(Fields.ClosureNotes);

        if (f.Value.GetValueOrDefault(Fields.Status) is "completed" && Text(f.Get(Fields.ClosureNotes)) is null)
            f.Errors[Fields.ClosureNotes] = "ملاحظات الإغلاق مطلوبة عند إكمال الإجراء";

        return Result(f.Value, f.Errors);
    }

    // ── dashboard settings ──────────────────────────────────────────────────
    public static ValidationResult<AppSettingsVm> ValidateAppSettings(Dictionary<string, object?>? input)
    {
        var o = AsObject(input);
        var errors = new Dictionary<string, string>();

        int Read(string key, int min, int max)
        {
            var v = IntValue(o.GetValueOrDefault(key));
            if (v is { Valid: true, Value: null }) errors[key] = Msg.Required;
            else if (!v.Valid) errors[key] = Msg.Integer;
            else if (v.Value < min || v.Value > max) errors[key] = $"يجب أن تكون القيمة بين {min} و{max}";
            return v.Value ?? 0;
        }

        var staleUpdateDays = Read("staleUpdateDays", 1, 180);
        var varianceOnTrack = Read("varianceOnTrack", -50, 0);
        var nearDeadlineDays = Read("nearDeadlineDays", 1, 365);
        // The "needs attention" limit never exceeds the "on track" limit; if the latter is invalid, cap at 0.
        var varianceAttention = Read("varianceAttention", -100, errors.ContainsKey("varianceOnTrack") ? 0 : varianceOnTrack);

        return Result(new AppSettingsVm
        {
            staleUpdateDays = staleUpdateDays,
            varianceOnTrack = varianceOnTrack,
            varianceAttention = varianceAttention,
            nearDeadlineDays = nearDeadlineDays,
        }, errors);
    }
}
