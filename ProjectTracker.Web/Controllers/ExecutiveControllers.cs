using System.Text.Json;
using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static ProjectTracker.Web.Services.PortfolioQueries;

namespace ProjectTracker.Web.Controllers;

/// <summary>Risks, issues and blockers of a project.</summary>
[Route("api/risks")]
public sealed class RisksController(SqlDb db, ProjectAccess access, AuditService audit) : ApiControllerBase
{
    private static readonly Dictionary<string, string[]> StatusFilters = new()
    {
        ["open"] = ["open"], ["mitigating"] = ["mitigating"], ["closed"] = ["closed"], ["active"] = ["open", "mitigating"],
    };

    private static readonly SetColumn[] Columns =
    [
        new("type", SqlKind.Text, 20), new("title", SqlKind.Text, 300), new("description", SqlKind.Text), new("severity", SqlKind.Text, 20),
        new("probability", SqlKind.Text, 20), new(Fields.Status, SqlKind.Text, 20), new(Fields.OwnerId, SqlKind.Guid), new(Fields.MilestoneId, SqlKind.Guid),
        new("due_date", SqlKind.Date), new("mitigation", SqlKind.Text),
    ];

    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var projectId = Query("project_id");
        var status = Query(Fields.Status);
        if (projectId is not null && !Js.IsUuid(projectId)) return Error("معرّف المشروع غير صالح", 400);
        if (status is not null && !StatusFilters.ContainsKey(status)) return Error("حالة غير معروفة", 400);

        var (page, pageSize, offset) = Paging(50, 200);
        var parameters = new List<Microsoft.Data.SqlClient.SqlParameter>
        {
            SqlDb.Guid("@pid", projectId), SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize),
        };
        var where = new List<string> { "(@pid IS NULL AND ISNULL(p.status, 'active') <> 'cancelled' OR r.project_id = @pid)" };
        if (status is not null)
        {
            var names = StatusFilters[status].Select((s, i) =>
            {
                parameters.Add(SqlDb.Text($"@st{i}", s, 20));
                return $"@st{i}";
            }).ToList();
            where.Add($"r.status IN ({string.Join(", ", names)})");
        }
        var whereSql = string.Join(" AND ", where);
        var sets = await db.QueryMultiAsync($"""
            {RiskSelect}
            WHERE {whereSql}
            ORDER BY CASE r.status WHEN 'closed' THEN 1 ELSE 0 END,
                     CASE r.severity WHEN 'critical' THEN 0 WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
                     r.created_at DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM risks_issues r JOIN projects p ON p.id = r.project_id WHERE {whereSql};
            SELECT COUNT(*) AS allTotal FROM risks_issues r JOIN projects p ON p.id = r.project_id WHERE {where[0]};
            """, parameters);
        var total = sets[1].FirstOrDefault()?.Int("total") ?? sets[0].Count;
        var allTotal = sets[2].FirstOrDefault()?.Int("allTotal") ?? total;
        return Json(new { risks = sets[0].Select(MapRisk).ToList(), allTotal, pagination = PaginationMeta(page, pageSize, total) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Create()
    {
        var body = await JsonBody.ReadAsync(Request);
        var projectId = body?.String("project_id");
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Project, projectId);
        if (denied is not null) return denied;

        var checkedRisk = PortfolioValidation.ValidateRisk(body!.ToDictionary());
        if (!checkedRisk.Ok) return ApiResults.ValidationFailed(checkedRisk.Errors);
        var v = checkedRisk.Value;
        string? Str(string k) => v.GetValueOrDefault(k) as string;

        var refErrors = await access.CheckReferencesAsync(projectId!, [new(Fields.MilestoneId, Str(Fields.MilestoneId))], Fields.OwnerId, Str(Fields.OwnerId));
        if (refErrors.Count > 0) return ApiResults.ValidationFailed(refErrors);

        var inserted = await db.FirstAsync(
            """
            INSERT INTO risks_issues (project_id, milestone_id, type, title, description, severity, probability,
                                      status, owner_id, due_date, mitigation, created_by)
            OUTPUT INSERTED.id
            VALUES (@project_id, @milestone_id, @type, @title, @description, @severity, @probability,
                    @status, @owner_id, @due_date, @mitigation, @created_by)
            """,
            [
                SqlDb.Guid("@project_id", projectId), SqlDb.Guid("@milestone_id", Str(Fields.MilestoneId)), SqlDb.Text("@type", Str("type"), 20),
                SqlDb.Text("@title", Str("title"), 300), SqlDb.Text("@description", Str("description")), SqlDb.Text("@severity", Str("severity"), 20),
                SqlDb.Text("@probability", Str("probability"), 20), SqlDb.Text("@status", Str(Fields.Status) ?? "open", 20),
                SqlDb.Guid("@owner_id", Str(Fields.OwnerId)), SqlDb.Date("@due_date", Str("due_date")), SqlDb.Text("@mitigation", Str("mitigation")),
                SqlDb.Guid("@created_by", Js.IsUuid(Me.Id) ? Me.Id : null),
            ]);
        var newId = inserted!.Str("id")!;
        var row = await db.FirstAsync($"{RiskSelect} WHERE r.id = @id", [SqlDb.Guid("@id", newId)]);

        await audit.WriteAsync(Me.Id, "create", "risk", newId, projectId, new { type = Str("type"), title = Str("title"), severity = Str("severity"), status = Str(Fields.Status) });
        return Json(new { risk = MapRisk(row!) }, 201);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id)
    {
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Risk, id);
        if (denied is not null) return denied;

        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();
        var checkedRisk = PortfolioValidation.ValidateRisk(body.ToDictionary(), partial: true);
        if (!checkedRisk.Ok) return ApiResults.ValidationFailed(checkedRisk.Errors);
        var v = checkedRisk.Value;
        if (v.Count == 0) return Error("لا توجد حقول للتعديل", 400);

        var before = await db.FirstAsync($"{RiskSelect} WHERE r.id = @id", [SqlDb.Guid("@id", id)]);
        if (before is null) return Error("العنصر غير موجود أو حُذف", 404);
        var projectId = before.Str("project_id")!;

        var refErrors = await access.CheckReferencesAsync(projectId,
            [new(Fields.MilestoneId, v.GetValueOrDefault(Fields.MilestoneId) as string)], Fields.OwnerId, v.GetValueOrDefault(Fields.OwnerId) as string);
        if (refErrors.Count > 0) return ApiResults.ValidationFailed(refErrors);

        var (sets, parameters) = SqlSet.Build(Columns, v);
        parameters.Add(SqlDb.Guid("@id", id));
        await db.ExecuteAsync($"UPDATE risks_issues SET {string.Join(", ", sets)}, updated_at = GETDATE() WHERE id = @id", parameters);

        var row = await db.FirstAsync($"{RiskSelect} WHERE r.id = @id", [SqlDb.Guid("@id", id)]);
        var risk = MapRisk(row!);

        var previous = SqlSet.AsLookup(MapRisk(before));
        var after = SqlSet.AsLookup(risk);
        var changes = new Dictionary<string, object?>();
        foreach (var key in v.Keys)
            changes[key] = new { from = previous.TryGetValue(key, out var f) ? f : (JsonElement?)null, to = after.TryGetValue(key, out var t) ? t : (JsonElement?)null };
        await audit.WriteAsync(Me.Id, "update", "risk", id, projectId, changes);
        return Json(new { risk });
    }
}

/// <summary>Decisions and follow-up actions recorded in the meeting.</summary>
[Route("api/meeting-actions")]
public sealed class MeetingActionsController(SqlDb db, ProjectAccess access, AuditService audit) : ApiControllerBase
{
    private static readonly Dictionary<string, string[]> StatusFilters = new()
    {
        ["new"] = ["new"], ["in_progress"] = ["in_progress"], ["completed"] = ["completed"], ["cancelled"] = ["cancelled"], ["open"] = ["new", "in_progress"],
    };

    private static readonly SetColumn[] Columns =
    [
        new("meeting_date", SqlKind.Date), new("action_type", SqlKind.Text, 20), new("description", SqlKind.Text), new(Fields.OwnerId, SqlKind.Guid),
        new("supporting_department", SqlKind.Text, 200), new("due_date", SqlKind.Date), new(Fields.Status, SqlKind.Text, 20),
        new("decision_notes", SqlKind.Text), new(Fields.ClosureNotes, SqlKind.Text),
    ];

    /// <summary>A project manager updates only the status and the closure notes of an action.</summary>
    private static readonly HashSet<string> ProjectManagerFields = [Fields.Status, Fields.ClosureNotes];

    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var projectId = Query("project_id");
        var ownerId = Query(Fields.OwnerId);
        var status = Query(Fields.Status);
        var overdue = Query("overdue") == "1";
        if (projectId is not null && !Js.IsUuid(projectId)) return Error("معرّف المشروع غير صالح", 400);
        if (ownerId is not null && !Js.IsUuid(ownerId)) return Error("معرّف المسؤول غير صالح", 400);
        if (status is not null && !StatusFilters.ContainsKey(status)) return Error("حالة غير معروفة", 400);

        var today = PortfolioCalc.TodayDateOnly();
        var (page, pageSize, offset) = Paging(50, 200);
        var parameters = new List<Microsoft.Data.SqlClient.SqlParameter>
        {
            SqlDb.Guid("@pid", projectId), SqlDb.Guid("@owner", ownerId), SqlDb.Date("@today", today),
            SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize),
        };
        var where = new List<string>
        {
            "(@pid IS NULL AND ISNULL(p.status, 'active') <> 'cancelled' OR a.project_id = @pid)",
            "(@owner IS NULL OR a.owner_id = @owner)",
        };
        if (status is not null)
        {
            var names = StatusFilters[status].Select((s, i) =>
            {
                parameters.Add(SqlDb.Text($"@st{i}", s, 20));
                return $"@st{i}";
            }).ToList();
            where.Add($"a.status IN ({string.Join(", ", names)})");
        }
        if (overdue) where.Add("a.status IN ('new', 'in_progress') AND a.due_date < @today");
        var whereSql = string.Join(" AND ", where);

        var sets = await db.QueryMultiAsync($"""
            {ActionSelect}
            WHERE {whereSql}
            ORDER BY CASE WHEN a.status IN ('new', 'in_progress') THEN 0 ELSE 1 END,
                     CASE WHEN a.due_date IS NULL THEN 1 ELSE 0 END, a.due_date, a.created_at DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM meeting_actions a JOIN projects p ON p.id = a.project_id WHERE {whereSql};
            """, parameters);
        var total = sets[1].FirstOrDefault()?.Int("total") ?? sets[0].Count;
        return Json(new { actions = sets[0].Select(r => MapMeetingAction(r, today)).ToList(), pagination = PaginationMeta(page, pageSize, total) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Create()
    {
        var body = await JsonBody.ReadAsync(Request);
        var projectId = body?.String("project_id");
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Project, projectId);
        if (denied is not null) return denied;

        var checkedAction = PortfolioValidation.ValidateMeetingAction(body!.ToDictionary());
        if (!checkedAction.Ok) return ApiResults.ValidationFailed(checkedAction.Errors);
        var v = checkedAction.Value;
        string? Str(string k) => v.GetValueOrDefault(k) as string;

        var refErrors = await access.CheckReferencesAsync(projectId!, null, Fields.OwnerId, Str(Fields.OwnerId));
        if (refErrors.Count > 0) return ApiResults.ValidationFailed(refErrors);

        var inserted = await db.FirstAsync(
            """
            INSERT INTO meeting_actions (project_id, meeting_date, action_type, description, owner_id,
                                         supporting_department, due_date, status, decision_notes, closure_notes, created_by)
            OUTPUT INSERTED.id
            VALUES (@project_id, @meeting_date, @action_type, @description, @owner_id,
                    @supporting_department, @due_date, @status, @decision_notes, @closure_notes, @created_by)
            """,
            [
                SqlDb.Guid("@project_id", projectId), SqlDb.Date("@meeting_date", Str("meeting_date")), SqlDb.Text("@action_type", Str("action_type"), 20),
                SqlDb.Text("@description", Str("description")), SqlDb.Guid("@owner_id", Str(Fields.OwnerId)),
                SqlDb.Text("@supporting_department", Str("supporting_department"), 200), SqlDb.Date("@due_date", Str("due_date")),
                SqlDb.Text("@status", Str(Fields.Status) ?? "new", 20), SqlDb.Text("@decision_notes", Str("decision_notes")),
                SqlDb.Text("@closure_notes", Str(Fields.ClosureNotes)), SqlDb.Guid("@created_by", Js.IsUuid(Me.Id) ? Me.Id : null),
            ]);
        var newId = inserted!.Str("id")!;
        var row = await db.FirstAsync($"{ActionSelect} WHERE a.id = @id", [SqlDb.Guid("@id", newId)]);

        await audit.WriteAsync(Me.Id, "create", "meeting_action", newId, projectId, new { action_type = Str("action_type"), status = Str(Fields.Status), due_date = Str("due_date") });
        return Json(new { action = MapMeetingAction(row!, PortfolioCalc.TodayDateOnly()) }, 201);
    }

    /// <summary>Closing notes cannot be cleared on a completed action unless the status moves away from completed.</summary>
    private static bool ClosureNotesMissing(Dictionary<string, object?> values, bool sentClosure, Row before) =>
        !values.ContainsKey(Fields.Status) && sentClosure && before.Str(Fields.Status) == "completed"
        && string.IsNullOrEmpty(values.GetValueOrDefault(Fields.ClosureNotes) as string);

    private static Dictionary<string, object?> ChangeDetails(IEnumerable<string> keys, Dictionary<string, JsonElement> previous, Dictionary<string, JsonElement> after)
    {
        var changes = new Dictionary<string, object?>();
        foreach (var key in keys)
            changes[key] = new { from = previous.TryGetValue(key, out var f) ? f : (JsonElement?)null, to = after.TryGetValue(key, out var t) ? t : (JsonElement?)null };
        return changes;
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id)
    {
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Action, id);
        if (denied is not null) return denied;

        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        if (Me.Role != "admin" && body.Keys.Any(k => Columns.Any(c => c.Column == k) && !ProjectManagerFields.Contains(k)))
            return Error("مدير المشروع يحدّث حالة الإجراء وملاحظات الإغلاق فقط؛ بقية الحقول يعدّلها مدير النظام.", 403);

        var before = await db.FirstAsync($"{ActionSelect} WHERE a.id = @id", [SqlDb.Guid("@id", id)]);
        if (before is null) return Error("العنصر غير موجود أو حُذف", 404);
        var projectId = before.Str("project_id")!;

        var sentClosure = body.Has(Fields.ClosureNotes);
        var input = new Dictionary<string, object?>();
        foreach (var column in Columns.Select(c => c.Column).Where(body.Has)) input[column] = body.Raw(column);
        if (!sentClosure && input.ContainsKey(Fields.Status)) input[Fields.ClosureNotes] = before.Str(Fields.ClosureNotes);

        var checkedAction = PortfolioValidation.ValidateMeetingAction(input, partial: true);
        if (!checkedAction.Ok) return ApiResults.ValidationFailed(checkedAction.Errors);
        var v = checkedAction.Value;
        if (!sentClosure) v.Remove(Fields.ClosureNotes);
        if (v.Count == 0) return Error("لا توجد حقول للتعديل", 400);
        if (ClosureNotesMissing(v, sentClosure, before))
            return ApiResults.ValidationFailed(new Dictionary<string, string> { [Fields.ClosureNotes] = "ملاحظات الإغلاق مطلوبة عند إكمال الإجراء" });

        var refErrors = await access.CheckReferencesAsync(projectId, null, Fields.OwnerId, v.GetValueOrDefault(Fields.OwnerId) as string);
        if (refErrors.Count > 0) return ApiResults.ValidationFailed(refErrors);

        var (sets, parameters) = SqlSet.Build(Columns, v);
        parameters.Add(SqlDb.Guid("@id", id));
        await db.ExecuteAsync($"UPDATE meeting_actions SET {string.Join(", ", sets)}, updated_at = GETDATE() WHERE id = @id", parameters);

        var today = PortfolioCalc.TodayDateOnly();
        var row = await db.FirstAsync($"{ActionSelect} WHERE a.id = @id", [SqlDb.Guid("@id", id)]);
        var action = MapMeetingAction(row!, today);

        var previous = SqlSet.AsLookup(MapMeetingAction(before, today));
        var after = SqlSet.AsLookup(action);
        await audit.WriteAsync(Me.Id, "update", "meeting_action", id, projectId, ChangeDetails(v.Keys, previous, after));
        return Json(new { action });
    }
}

/// <summary>Weekly executive updates (insert-only history).</summary>
[Route("api/project-updates")]
public sealed class ProjectUpdatesController(SqlDb db, ProjectAccess access, AuditService audit) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var projectId = Query("project_id");
        if (!Js.IsUuid(projectId)) return Error("حدد المشروع (project_id صالح)", 400);
        var (page, pageSize, offset) = Paging(20, 100);
        var sets = await db.QueryMultiAsync($"""
            {ProjectUpdateSelect}
            WHERE u.project_id = @pid
            ORDER BY u.reporting_date DESC, u.created_at DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM project_updates WHERE project_id = @pid;
            """, [SqlDb.Guid("@pid", projectId), SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize)]);
        var total = sets[1].FirstOrDefault()?.Int("total") ?? sets[0].Count;
        return Json(new { updates = sets[0].Select(MapProjectUpdate).ToList(), pagination = PaginationMeta(page, pageSize, total) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Create()
    {
        var body = await JsonBody.ReadAsync(Request);
        var projectId = body?.String("project_id");
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Project, projectId);
        if (denied is not null) return denied;

        var checkedUpdate = PortfolioValidation.ValidateProjectUpdate(body!.ToDictionary(), PortfolioCalc.TodayDateOnly());
        if (!checkedUpdate.Ok) return ApiResults.ValidationFailed(checkedUpdate.Errors);
        var v = checkedUpdate.Value;

        var refErrors = await access.CheckReferencesAsync(projectId!,
            [new("current_milestone_id", v.current_milestone_id), new("next_milestone_id", v.next_milestone_id)]);
        if (refErrors.Count > 0) return ApiResults.ValidationFailed(refErrors);

        var variance = PortfolioCalc.ComputeVariance(v.actual_progress, v.planned_progress);
        var inserted = await db.FirstAsync(
            """
            INSERT INTO project_updates (
              project_id, reporting_date, executive_summary, actual_progress, planned_progress, variance_percent,
              health_status, current_milestone_id, next_milestone_id, forecast_end_date, delay_reason,
              support_required, support_from, support_impact, support_due_date,
              decision_required, decision_from, decision_needed_by, decision_status, updated_by)
            OUTPUT INSERTED.id
            VALUES (
              @project_id, @reporting_date, @executive_summary, @actual_progress, @planned_progress, @variance_percent,
              @health_status, @current_milestone_id, @next_milestone_id, @forecast_end_date, @delay_reason,
              @support_required, @support_from, @support_impact, @support_due_date,
              @decision_required, @decision_from, @decision_needed_by, @decision_status, @updated_by)
            """,
            [
                SqlDb.Guid("@project_id", projectId), SqlDb.Date("@reporting_date", v.reporting_date), SqlDb.Text("@executive_summary", v.executive_summary),
                SqlDb.Int("@actual_progress", v.actual_progress), SqlDb.Int("@planned_progress", v.planned_progress), SqlDb.Int("@variance_percent", variance),
                SqlDb.Text("@health_status", v.health_status, 20), SqlDb.Guid("@current_milestone_id", v.current_milestone_id),
                SqlDb.Guid("@next_milestone_id", v.next_milestone_id), SqlDb.Date("@forecast_end_date", v.forecast_end_date),
                SqlDb.Text("@delay_reason", v.delay_reason), SqlDb.Text("@support_required", v.support_required),
                SqlDb.Text("@support_from", v.support_from, 200), SqlDb.Text("@support_impact", v.support_impact),
                SqlDb.Date("@support_due_date", v.support_due_date), SqlDb.Text("@decision_required", v.decision_required),
                SqlDb.Text("@decision_from", v.decision_from, 200), SqlDb.Date("@decision_needed_by", v.decision_needed_by),
                SqlDb.Text("@decision_status", v.decision_status, 20), SqlDb.Guid("@updated_by", Js.IsUuid(Me.Id) ? Me.Id : null),
            ]);
        var newId = inserted!.Str("id")!;
        var row = await db.FirstAsync($"{ProjectUpdateSelect} WHERE u.id = @id", [SqlDb.Guid("@id", newId)]);

        await audit.WriteAsync(Me.Id, "create", "project_update", newId, projectId,
            new { reporting_date = v.reporting_date, health_status = v.health_status, variance_percent = variance });
        return Json(new { update = MapProjectUpdate(row!) }, 201);
    }
}

/// <summary>The follow-up dashboards: portfolio, one project's summary, and the executive KPI dashboard.</summary>
[Route("api")]
public sealed class PortfolioController(PortfolioQueries queries, SqlDb db) : ApiControllerBase
{
    [HttpGet("portfolio")]
    public async Task<IActionResult> Portfolio()
    {
        return Json(await queries.LoadPortfolioAsync(new PortfolioUser(Me.Id, Me.Role)));
    }

    [HttpGet("portfolio/{id}")]
    public async Task<IActionResult> Summary(string id)
    {
        if (!Js.IsUuid(id)) return Error("المشروع غير موجود", 404);
        var data = await queries.LoadProjectSummaryAsync(id, new PortfolioUser(Me.Id, Me.Role));
        return data is null ? Error("المشروع غير موجود", 404) : Json(data);
    }

    [HttpGet("dashboard/executive")]
    public async Task<IActionResult> Executive()
    {
        var data = await queries.LoadExecutiveDashboardAsync(new PortfolioUser(Me.Id, Me.Role));
        Response.Headers.CacheControl = "no-store";
        return Json(data);
    }

    /// <summary>Active employees, for the "owner" pickers.</summary>
    [HttpGet("people")]
    public async Task<IActionResult> People()
    {
        var rows = await db.QueryAsync("SELECT id, full_name FROM profiles WHERE ISNULL(status, 'active') = 'active' ORDER BY full_name");
        return Json(new { people = rows.Select(r => new { id = r.Str("id"), full_name = r.Str("full_name") }).ToList() });
    }
}

/// <summary>The audit trail (admin only).</summary>
[Authorize(Policy = TrackerAuth.AdminOnlyPolicy)]
[Route("api/audit-log")]
public sealed class AuditLogController(SqlDb db) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var projectId = Query("project_id");
        var entityType = Query("entity_type");
        var action = Query("action");
        if (projectId is not null && !Js.IsUuid(projectId)) return Error("معرّف المشروع غير صالح", 400);

        var (page, pageSize, offset) = Paging(50, 200);
        const string where = "(@pid IS NULL OR l.project_id = @pid) AND (@entity IS NULL OR l.entity_type = @entity) AND (@action IS NULL OR l.action = @action)";
        var sets = await db.QueryMultiAsync($"""
            SELECT l.*, pr.full_name AS user_name
            FROM audit_log l
            LEFT JOIN profiles pr ON pr.id = l.user_id
            WHERE {where}
            ORDER BY l.created_at DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM audit_log l WHERE {where};
            """, [SqlDb.Guid("@pid", projectId), SqlDb.Text("@entity", entityType, 50), SqlDb.Text("@action", action, 20), SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize)]);
        var total = sets[1].FirstOrDefault()?.Int("total") ?? sets[0].Count;
        return Json(new { entries = sets[0].Select(MapAuditEntry).ToList(), pagination = PaginationMeta(page, pageSize, total) });
    }
}

/// <summary>Dashboard thresholds: everyone reads them, only the admin changes them.</summary>
[Route("api/app-settings")]
public sealed class AppSettingsController(AppSettingsService settings, AuditService audit) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Get()
    {
        return Json(new { settings = await settings.LoadAsync() });
    }

    [Authorize(Policy = TrackerAuth.AdminOnlyPolicy)]
    [HttpPatch("")]
    public async Task<IActionResult> Update()
    {
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        var current = await settings.LoadAsync();
        var merged = new Dictionary<string, object?>
        {
            ["staleUpdateDays"] = (long)current.staleUpdateDays, ["varianceOnTrack"] = (long)current.varianceOnTrack,
            ["varianceAttention"] = (long)current.varianceAttention, ["nearDeadlineDays"] = (long)current.nearDeadlineDays,
        };
        foreach (var key in merged.Keys.Where(body.Has).ToList()) merged[key] = body.Raw(key);

        var checkedSettings = PortfolioValidation.ValidateAppSettings(merged);
        if (!checkedSettings.Ok) return ApiResults.ValidationFailed(checkedSettings.Errors);

        await settings.SaveAsync(checkedSettings.Value, Me.Id);
        await audit.WriteAsync(Me.Id, "update", "app_settings", null, null, new { from = current, to = checkedSettings.Value });
        return Json(new { settings = checkedSettings.Value });
    }
}

/// <summary>Trigger of the notification cycle: a scheduled task with the secret, or the admin.</summary>
[AllowAnonymous]
[Route("api/notifications")]
public sealed class NotificationsController(NotificationService notifications, AppConfig config) : ApiControllerBase
{
    private bool SecretMatches(string? provided)
    {
        var expected = config.NotifySecret;
        if (string.IsNullOrEmpty(expected) || expected.Length < 16 || string.IsNullOrEmpty(provided)) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(provided);
        var b = System.Text.Encoding.UTF8.GetBytes(expected);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromHeader(Name = "x-notify-secret")] string? notifySecret)
    {
        var bySecret = SecretMatches(notifySecret);
        if (!bySecret)
        {
            // Public route: authenticate explicitly (this also verifies the session against the database).
            var auth = await HttpContext.AuthenticateAsync(TrackerAuth.Scheme);
            if (auth.Principal?.SessionUser().Role != "admin")
                return Error("هذا المسار للمهمة المجدولة أو لمدير النظام فقط", 403);
        }

        var forceWeekly = Query("weekly") == "force";
        var summary = await notifications.RunCycleAsync(forceWeekly);
        var now = notifications.CurrentZonedNow();
        Response.Headers.CacheControl = "no-store";
        return Json(new
        {
            summary.weekly, summary.delays, summary.projectDelays,
            now = new { weekday = now.Weekday, hour = now.Hour, date = now.Date },
            triggeredBy = bySecret ? "schedule" : "admin",
        });
    }
}

/// <summary>Any other /api/* path answers JSON (not an HTML page), after the session check.</summary>
[Route("api/{**path}")]
public sealed class ApiFallbackController : ApiControllerBase
{
    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE")]
    public IActionResult NotFoundJson() => Error("مسار الـ API المطلوب غير موجود", 404);
}
