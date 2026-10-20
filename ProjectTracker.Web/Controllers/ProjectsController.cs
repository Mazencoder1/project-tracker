using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;
using ProjectTracker.Web.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ProjectTracker.Web.Controllers;

[Route("api/projects")]
public sealed class ProjectsController(SqlDb db, AuditService audit, IProjectEmailNotifier emailNotifier) : ApiControllerBase
{
    private static readonly string[] AllowedStatuses = ["active", "completed", "delayed", "cancelled"];

    private static readonly Dictionary<string, string> SortColumns = new()
    {
        ["name"] = "p.name",
        ["start"] = "p.start_date",
        ["end"] = "p.end_date",
        ["weeks"] = "p.total_weeks",
        [Fields.Status] = "CASE p.status WHEN 'delayed' THEN 0 WHEN 'completed' THEN 1 WHEN 'cancelled' THEN 2 ELSE 3 END",
        ["created"] = "p.created_at",
    };

    /// <summary>
    /// The admin and the deputy see every project; a project manager sees the projects assigned to them.
    /// Optional: ?q= (name / code / department), ?status=, ?sort= + ?dir=. <c>stats</c> ignores the filters.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var all = Me.Role is "admin" or "deputy" || Query("scope") == "all";
        var status = Query(Fields.Status);
        if (status is not null && !AllowedStatuses.Contains(status)) return Error("حالة غير معروفة", 400);
        var search = SearchPattern();
        var (page, pageSize, offset) = Paging(20, 100);
        var orderBy = OrderBy(SortColumns, "p.created_at DESC");

        // Scope = who may see the row; filters = what the list is narrowed to. Stats count the scope only.
        var scope = all ? "1 = 1" : "EXISTS (SELECT 1 FROM assignments a WHERE a.project_id = p.id AND a.user_id = @user_id)";
        const string filters = """
            (@status IS NULL OR p.status = @status)
            AND (@q IS NULL OR p.name LIKE @q ESCAPE '\' OR p.project_code LIKE @q ESCAPE '\' OR p.department LIKE @q ESCAPE '\')
            """;
        var sets = await db.QueryMultiAsync($"""
            SELECT p.* FROM projects p
            WHERE {scope} AND {filters}
            ORDER BY {orderBy}, p.id
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM projects p WHERE {scope} AND {filters};

            SELECT COUNT(*) AS allTotal,
                   ISNULL(SUM(CASE WHEN p.status = 'active' THEN 1 ELSE 0 END), 0) AS activeTotal,
                   ISNULL(SUM(CASE WHEN ISNULL(p.status, 'active') NOT IN ('completed', 'cancelled')
                                    AND p.end_date < CAST(GETDATE() AS date) THEN 1 ELSE 0 END), 0) AS overdueTotal
            FROM projects p WHERE {scope};
            """,
            [
                SqlDb.Guid("@user_id", all ? null : Me.Id), SqlDb.Text("@status", status, 20), SqlDb.Text("@q", search, 210),
                SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize),
            ]);

        var rows = sets[0];
        var total = sets[1].FirstOrDefault()?.Int("total") ?? rows.Count;
        var stats = sets[2].FirstOrDefault();
        var activeCount = stats?.Int("activeTotal") ?? 0;
        return Json(new
        {
            projects = rows.Select(r => r.ToJson()).ToList(),
            activeCount,
            stats = new { total = stats?.Int("allTotal") ?? total, active = activeCount, overdue = stats?.Int("overdueTotal") ?? 0 },
            pagination = PaginationMeta(page, pageSize, total),
        });
    }

    private static bool Truthy(object? v) => v switch
    {
        null => false,
        string s => s.Trim().Length > 0,
        bool b => b,
        long l => l != 0,
        double d => d != 0,
        _ => true,
    };

    /// <summary>Name, description, schedule, optional fields and department of a new project; every problem is returned at once.</summary>
    private static (string Name, ProjectRules.ScheduleValue Schedule, ProjectRules.ProjectExtras Extras, Dictionary<string, string> Errors) ValidateNewProject(
        JsonBody body, DepartmentService departments)
    {
        var errors = new Dictionary<string, string>();
        var name = body.String("name")?.Trim() ?? "";
        if (name.Length == 0) errors["name"] = "اسم المشروع مطلوب";
        else if (name.Length > ProjectRules.ProjectNameMax) errors["name"] = $"اسم المشروع يجب ألا يزيد على {ProjectRules.ProjectNameMax} حرف";

        if ((body.String(Fields.Description)?.Trim().Length ?? 0) > ProjectRules.ProjectDescriptionMax)
            errors[Fields.Description] = $"الوصف يجب ألا يزيد على {ProjectRules.ProjectDescriptionMax} حرف";

        var (schedule, scheduleErrors) = ProjectRules.ValidateProjectSchedule(body.Raw(Fields.StartDate), body.Raw(Fields.EndDate));
        RequireDate(body, scheduleErrors, Fields.StartDate, "تاريخ البداية مطلوب.");
        RequireDate(body, scheduleErrors, Fields.EndDate, "تاريخ النهاية مطلوب.");
        var (extras, extraErrors) = ProjectRules.ParseProjectExtras(body);
        foreach (var (k, v) in scheduleErrors) errors[k] = v;
        foreach (var (k, v) in extraErrors) errors[k] = v;
        if (!errors.ContainsKey(Fields.Department) && DepartmentProblem(departments, extras, null) is { } deptProblem) errors[Fields.Department] = deptProblem;
        return (name, schedule, extras, errors);
    }

    private static void RequireDate(JsonBody body, Dictionary<string, string> errors, string field, string message)
    {
        if (!Truthy(body.Raw(field)) && !errors.ContainsKey(field)) errors[field] = message;
    }

    /// <summary>New project — admin and deputy. The number of weeks is derived from the two dates.</summary>
    /// <summary>
    /// The owning department must come from the configured list (the UI offers a drop-down only). The project's current
    /// value stays accepted, so projects saved before the list existed can still be edited.
    /// </summary>
    private static string? DepartmentProblem(DepartmentService departments, ProjectRules.ProjectExtras extras, string? current)
    {
        if (!extras.HasDepartment || extras.Department is null || extras.Department == current) return null;
        return departments.GetDepartments().Any(d => d.Name == extras.Department) ? null : "اختر الإدارة المالكة من القائمة.";
    }

    [Authorize(Policy = TrackerAuth.AdminOrDeputyPolicy)]
    [HttpPost("")]
    public async Task<IActionResult> Create([FromServices] DepartmentService departments)
    {
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        var (name, schedule, extras, errors) = ValidateNewProject(body, departments);
        if (errors.Count > 0) return ApiResults.ValidationFailed(errors);

        int? totalWeeks = schedule.StartDate is not null && schedule.EndDate is not null
            ? ProjectRules.CalcProjectWeeks(schedule.StartDate, schedule.EndDate)
            : null;

        var inserted = await db.FirstAsync(
            """
            INSERT INTO projects (name, description, start_date, end_date, total_weeks, project_code, department, priority)
            OUTPUT INSERTED.id
            VALUES (@name, @description, @start_date, @end_date, @total_weeks, @project_code, @department, @priority)
            """,
            [
                SqlDb.Text("@name", name), SqlDb.Text("@description", body.String(Fields.Description) ?? ""),
                SqlDb.Date("@start_date", schedule.StartDate), SqlDb.Date("@end_date", schedule.EndDate), SqlDb.Int("@total_weeks", totalWeeks),
                SqlDb.Text("@project_code", extras.ProjectCode, 50), SqlDb.Text("@department", extras.Department, 200), SqlDb.Text("@priority", extras.Priority, 20),
            ]);
        var id = inserted!.Str("id")!;

        var details = new Dictionary<string, object?> { ["name"] = name, [Fields.StartDate] = schedule.StartDate, [Fields.EndDate] = schedule.EndDate, ["total_weeks"] = totalWeeks };
        if (extras.HasProjectCode) details["project_code"] = extras.ProjectCode;
        if (extras.HasDepartment) details[Fields.Department] = extras.Department;
        if (extras.HasPriority) details["priority"] = extras.Priority;
        await audit.WriteAsync(Me.Id, "create", "project", id, id, details);
        return Json(new { success = true, id });
    }

    private sealed record Change(SqlParameter Parameter, object? Value);

    /// <summary>Status, name and description edits; returns the error response when one of them is invalid.</summary>
    private static JsonResult? CollectBasicChanges(JsonBody body, Dictionary<string, Change> changes)
    {
        if (body.Has(Fields.Status))
        {
            var status = body.Raw(Fields.Status)?.ToString() ?? "null";
            if (!AllowedStatuses.Contains(status)) return Error("حالة المشروع غير صحيحة", 400);
            changes[Fields.Status] = new(SqlDb.Text("@set_status", status, 20), body.Raw(Fields.Status));
        }
        if (body.Has("name"))
        {
            var name = body.String("name")?.Trim() ?? "";
            if (name.Length == 0 || name.Length > ProjectRules.ProjectNameMax)
                return Json(new { error = $"اسم المشروع مطلوب ({ProjectRules.ProjectNameMax} حرف كحد أقصى)", errors = new { name = "اسم المشروع مطلوب" } }, 400);
            changes["name"] = new(SqlDb.Text("@set_name", name, 200), name);
        }
        if (body.Has(Fields.Description))
        {
            var description = body.String(Fields.Description)?.Trim() ?? "";
            if (description.Length > ProjectRules.ProjectDescriptionMax)
                return ApiResults.ValidationFailed(new() { [Fields.Description] = $"الوصف يجب ألا يزيد على {ProjectRules.ProjectDescriptionMax} حرف" });
            changes[Fields.Description] = new(SqlDb.Text("@set_description", description), description);
        }
        return null;
    }

    private static void AddExtraChanges(ProjectRules.ProjectExtras extras, Dictionary<string, Change> changes)
    {
        if (extras.HasProjectCode) changes["project_code"] = new(SqlDb.Text("@set_project_code", extras.ProjectCode, 50), extras.ProjectCode);
        if (extras.HasDepartment) changes[Fields.Department] = new(SqlDb.Text("@set_department", extras.Department, 200), extras.Department);
        if (extras.HasPriority) changes["priority"] = new(SqlDb.Text("@set_priority", extras.Priority, 20), extras.Priority);
    }

    /// <summary>Date edits and the derived week count; refuses a duration shorter than an existing milestone.</summary>
    private async Task<IActionResult?> CollectScheduleChangesAsync(string id, JsonBody body, Row before, Dictionary<string, Change> changes)
    {
        var (schedule, scheduleErrors) = ProjectRules.ValidateProjectSchedule(
            body.Has(Fields.StartDate) ? body.Raw(Fields.StartDate) : before.Date(Fields.StartDate),
            body.Has(Fields.EndDate) ? body.Raw(Fields.EndDate) : before.Date(Fields.EndDate));
        if (schedule.StartDate is null && !scheduleErrors.ContainsKey(Fields.StartDate)) scheduleErrors[Fields.StartDate] = "تاريخ البداية مطلوب.";
        if (schedule.EndDate is null && !scheduleErrors.ContainsKey(Fields.EndDate)) scheduleErrors[Fields.EndDate] = "تاريخ النهاية مطلوب.";
        if (scheduleErrors.Count > 0) return ApiResults.ValidationFailed(scheduleErrors);

        var weeks = ProjectRules.CalcProjectWeeks(schedule.StartDate!, schedule.EndDate!)!.Value;
        var range = await db.FirstAsync("SELECT MAX(week_end) AS max_week_end FROM milestones WHERE project_id = @id", [SqlDb.Guid("@id", id)]);
        var maxMilestoneWeek = range?.Int("max_week_end") ?? 0;
        if (maxMilestoneWeek > weeks)
        {
            return Json(new
            {
                error = "لا يمكن تقليص مدة المشروع قبل تعديل مراحله.",
                errors = new { end_date = $"توجد مرحلة تنتهي في الأسبوع {maxMilestoneWeek}، بينما المدة الجديدة {ArabicCount.Format(weeks, "week")}." },
            }, 400);
        }
        if (body.Has(Fields.StartDate)) changes[Fields.StartDate] = new(SqlDb.Date("@set_start_date", schedule.StartDate), schedule.StartDate);
        if (body.Has(Fields.EndDate)) changes[Fields.EndDate] = new(SqlDb.Date("@set_end_date", schedule.EndDate), schedule.EndDate);
        changes["total_weeks"] = new(SqlDb.Int("@set_total_weeks", weeks), weeks);
        return null;
    }

    /// <summary>Edit a project (admin only). Shortening the duration below an existing milestone is refused.</summary>
    [Authorize(Policy = TrackerAuth.AdminOnlyPolicy)]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromServices] DepartmentService departments)
    {
        var body = await JsonBody.ReadAsync(Request);
        if (!Js.IsUuid(id)) return Error("معرّف غير صالح", 400);
        if (body is null) return ApiResults.BadBody();

        var changes = new Dictionary<string, Change>();
        if (CollectBasicChanges(body, changes) is { } basicFailure) return basicFailure;
        var (extras, extraErrors) = ProjectRules.ParseProjectExtras(body);
        if (extraErrors.Count > 0) return ApiResults.ValidationFailed(extraErrors);
        AddExtraChanges(extras, changes);

        var scheduleSent = body.Has(Fields.StartDate) || body.Has(Fields.EndDate);
        if (changes.Count == 0 && !scheduleSent) return Error("حالة المشروع غير صحيحة", 400);

        var before = await db.FirstAsync(
            "SELECT name, description, status, project_code, department, priority, start_date, end_date, total_weeks FROM projects WHERE id = @id",
            [SqlDb.Guid("@id", id)]);
        if (before is null) return Error("المشروع غير موجود", 404);
        if (DepartmentProblem(departments, extras, before.Str(Fields.Department)) is { } deptProblem)
            return ApiResults.ValidationFailed(new() { [Fields.Department] = deptProblem });

        if (scheduleSent && await CollectScheduleChangesAsync(id, body, before, changes) is { } scheduleFailure) return scheduleFailure;

        var parameters = new List<SqlParameter> { SqlDb.Guid("@id", id) };
        parameters.AddRange(changes.Values.Select(c => c.Parameter));
        var sets = changes.Select(c => $"{c.Key} = @set_{c.Key}");
        await db.ExecuteAsync($"UPDATE projects SET {string.Join(", ", sets)} WHERE id = @id", parameters);

        var details = new Dictionary<string, object?>();
        foreach (var (column, change) in changes)
        {
            object? from = column is Fields.StartDate or Fields.EndDate ? before.Date(column) : Row.ToJsonValue(before[column]);
            details[column] = new { from, to = change.Value };
        }
        await audit.WriteAsync(Me.Id, "update", "project", id, id, details);

        // Note: رسالة الإكمال عند انتقال فعلي فقط: الحالة السابقة ≠ completed والجديدة = completed (لا عند كل تعديل).
        // الحفظ تم بنجاح قبل هذا السطر، والمُرسِل لا يرمي استثناءً ويمنع التكرار عبر سجل البريد.
        if (changes.TryGetValue(Fields.Status, out var statusChange) && statusChange.Value as string == "completed" && before.Str(Fields.Status) != "completed")
            await emailNotifier.NotifyProjectCompletedAsync(id);

        return Json(new { success = true });
    }

    /// <summary>Permanent deletion of a project and everything under it (milestones, tasks, notes, updates, risks, actions); the audit trail is kept.</summary>
    [Authorize(Policy = TrackerAuth.AdminOnlyPolicy)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!Js.IsUuid(id)) return Error("معرّف غير صالح", 400);

        Row? existing;
        await using var tx = await db.Context.Database.BeginTransactionAsync();
        try
        {
            existing = await db.FirstAsync("SELECT id, name, project_code FROM projects WITH (UPDLOCK, HOLDLOCK) WHERE id = @id", [SqlDb.Guid("@id", id)]);
            if (existing is null)
            {
                await tx.RollbackAsync();
                return Error("المشروع غير موجود أو حُذف مسبقًا", 404);
            }

            await db.ExecuteAsync(
                """
                -- سجل التدقيق يبقى بعد الحذف (كان يُمسح مع المشروع فيضيع أثر من فعل ماذا).
                DECLARE @milestones TABLE (id UNIQUEIDENTIFIER NOT NULL);
                INSERT INTO @milestones (id) SELECT id FROM milestones WHERE project_id = @id;

                IF OBJECT_ID(N'dbo.EmailNotificationLog', N'U') IS NOT NULL
                  DELETE FROM dbo.EmailNotificationLog WHERE ProjectId = @id;
                DELETE FROM notification_log
                WHERE (kind IN ('assignment', 'project_delay') AND ref = LOWER(CONVERT(NVARCHAR(36), @id)))
                   OR (kind = 'milestone_delay' AND ref IN (
                        SELECT LOWER(CONVERT(NVARCHAR(36), id)) FROM @milestones
                      ));

                DELETE FROM meeting_actions WHERE project_id = @id;
                DELETE FROM risks_issues WHERE project_id = @id;
                DELETE FROM project_updates WHERE project_id = @id;
                DELETE n FROM phase_notes n JOIN @milestones m ON m.id = n.milestone_id;
                DELETE t FROM tasks t JOIN @milestones m ON m.id = t.milestone_id;
                DELETE FROM assignments WHERE project_id = @id;
                DELETE FROM milestones WHERE project_id = @id;
                DELETE FROM projects WHERE id = @id;
                """, [SqlDb.Guid("@id", id)]);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        await audit.WriteAsync(Me.Id, "delete", "project", id, id,
            new { hardDeleted = true, name = existing.Str("name"), project_code = existing.Str("project_code") });
        return Json(new { success = true, deleted = true });
    }
}
