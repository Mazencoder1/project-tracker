using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;
using ProjectTracker.Web.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTracker.Web.Controllers;

/// <summary>Assigning project managers to projects.</summary>
[Route("api/assignments")]
public sealed class AssignmentsController(SqlDb db, ProjectAccess access, IProjectEmailNotifier emailNotifier, AuditService audit) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery(Name = Fields.ProjectId)] string? projectId)
    {
        if (projectId is not null && !Js.IsUuid(projectId)) return Error("معرّف المشروع غير صالح", 400);

        var (page, pageSize, offset) = Paging(100, 300);
        var sets = await db.QueryMultiAsync(
            """
            SELECT a.user_id, p.full_name, p.email, p.role
            FROM assignments a
            JOIN profiles p ON p.id = a.user_id
            WHERE a.project_id = @project_id
            ORDER BY p.full_name
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM assignments a WHERE a.project_id = @project_id;
            """, [SqlDb.Guid("@project_id", projectId), SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize)]);
        var rows = sets[0];
        var total = sets[1].FirstOrDefault()?.Int("total") ?? rows.Count;

        // E-mail addresses are visible to the admin, the deputy and the project's own managers only.
        var showEmail = Me.Role is "admin" or "deputy" || (projectId is not null && await access.IsAssignedToProjectAsync(Me.Id, projectId));
        var assignments = rows.Select(r =>
        {
            var json = r.ToJson();
            if (!showEmail) json.Remove("email");
            return json;
        }).ToList();
        return Json(new { assignments, pagination = PaginationMeta(page, pageSize, total) });
    }

    private async Task<(IActionResult? Failure, string ProjectId, string UserId)> ReadIdsAsync()
    {
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return (ApiResults.BadBody(), "", "");
        var projectId = body.String(Fields.ProjectId);
        var userId = body.String("user_id");
        if (!Js.IsUuid(projectId)) return (Error("معرّف المشروع غير صالح", 400), "", "");
        if (!Js.IsUuid(userId)) return (Error("معرّف الموظف غير صالح", 400), "", "");
        return (null, projectId!, userId!);
    }

    [Authorize(Policy = TrackerAuth.AdminOrDeputyPolicy)]
    [HttpPost("")]
    public async Task<IActionResult> Assign()
    {
        try
        {
            var (failure, projectId, userId) = await ReadIdsAsync();
            if (failure is not null) return failure;

            var state = (await db.FirstAsync(
                """
                SELECT
                  (SELECT COUNT(*) FROM projects WHERE id = @project_id) AS project_found,
                  (SELECT TOP 1 name FROM projects WHERE id = @project_id) AS project_name,
                  (SELECT TOP 1 ISNULL(status, 'active') FROM profiles WHERE id = @user_id AND ISNULL(status, 'active') <> 'deleted') AS user_status,
                  (SELECT TOP 1 full_name FROM profiles WHERE id = @user_id) AS user_full_name,
                  (SELECT TOP 1 role FROM profiles WHERE id = @user_id) AS user_role,
                  (SELECT COUNT(*) FROM assignments WHERE project_id = @project_id AND user_id = @user_id) AS already
                """, [SqlDb.Guid("@project_id", projectId), SqlDb.Guid("@user_id", userId)]))!;
            if ((state.Int("project_found") ?? 0) == 0) return Error("المشروع غير موجود أو حُذف", 404);
            var userStatus = state.Str("user_status");
            if (userStatus is null) return Error("الموظف غير موجود أو حُذف", 404);
            if (userStatus != "active") return Error("حساب الموظف موقوف. فعّل الحساب أولًا ثم أسنده للمشروع.", 409);
            if (state.Str("user_role") != "pm") return Error("لا يُسنَد للمشروع إلا مدير مشروع.", 400);
            if ((state.Int("already") ?? 0) > 0) return Json(new { success = true, alreadyAssigned = true });

            var inserted = await db.FirstAsync("INSERT INTO assignments (project_id, user_id) OUTPUT INSERTED.id VALUES (@project_id, @user_id)",
                [SqlDb.Guid(Fields.ParamProjectId, projectId), SqlDb.Guid(Fields.ParamUserId, userId)]);

            await audit.WriteAsync(Me.Id, "create", "assignment", inserted!.Str("id"), projectId,
                new { user_id = userId, user_name = state.Str("user_full_name"), project_name = state.Str("project_name") });

            // Note: البريد بعد نجاح حفظ الإسناد فقط، ولا يُفشل الإسناد أبدًا (المُرسِل لا يرمي استثناءً).
            // مفتاح منع التكرار يحمل معرّف صف الإسناد: إسناد جديد (تغيير المسؤول أو إعادة إسناد) ⇒ رسالة جديدة، وإعادة الإرسال/التحديث ⇒ لا رسالة.
            var notified = await emailNotifier.NotifyProjectAssignedAsync(Guid.Parse(inserted.Str("id")!), projectId, userId);
            return Json(new { success = true, notified = notified == EmailNotifyOutcome.Sent });
        }
        catch (Exception ex) when (SqlDb.SqlErrorNumber(ex) is 2627 or 2601)
        {
            // A concurrent request may insert the same pair first; the unique key makes that a no-op.
            return Json(new { success = true, alreadyAssigned = true });
        }
    }

    [Authorize(Policy = TrackerAuth.AdminOrDeputyPolicy)]
    [HttpDelete("")]
    public async Task<IActionResult> Unassign()
    {
        var (failure, projectId, userId) = await ReadIdsAsync();
        if (failure is not null) return failure;
        var labels = await db.FirstAsync(
            """
            SELECT (SELECT TOP 1 name FROM projects WHERE id = @project_id) AS project_name,
                   (SELECT TOP 1 full_name FROM profiles WHERE id = @user_id) AS user_full_name
            """, [SqlDb.Guid("@project_id", projectId), SqlDb.Guid("@user_id", userId)]);
        var affected = await db.ExecuteAsync("DELETE FROM assignments WHERE project_id = @project_id AND user_id = @user_id",
            [SqlDb.Guid(Fields.ParamProjectId, projectId), SqlDb.Guid(Fields.ParamUserId, userId)]);
        if (affected == 0) return Error("الموظف غير مسند لهذا المشروع (ربما أُزيل مسبقًا).", 404);
        await audit.WriteAsync(Me.Id, "delete", "assignment", null, projectId,
            new { user_id = userId, user_name = labels?.Str("user_full_name"), project_name = labels?.Str("project_name") });
        return Json(new { success = true });
    }
}

/// <summary>Main tasks of a project (a milestone is a "phase" in the UI).</summary>
[Route("api/milestones")]
public sealed class MilestonesController(SqlDb db, ProjectAccess access, AuditService audit) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var projectId = Query(Fields.ProjectId);
        if (!Js.IsUuid(projectId)) return Error("معرّف المشروع مطلوب وصحيح.", 400);
        var (page, pageSize, offset) = Paging(50, 200);
        var sets = await db.QueryMultiAsync(
            """
            SELECT m.*,
              ISNULL((SELECT SUM(CAST(t.progress AS FLOAT) * (CASE WHEN t.week_end >= t.week_start THEN t.week_end - t.week_start + 1 ELSE 1 END))
                        / NULLIF(SUM(CASE WHEN t.week_end >= t.week_start THEN t.week_end - t.week_start + 1 ELSE 1 END), 0)
                      FROM tasks t WHERE t.milestone_id = m.id), 0) AS progress,
              (SELECT COUNT(*) FROM tasks t WHERE t.milestone_id = m.id) AS tasks_count
            FROM milestones m
            WHERE m.project_id = @project_id
            ORDER BY m.week_start, ISNULL(m.display_order, 0), m.name
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM milestones WHERE project_id = @project_id;
            """, [SqlDb.Guid("@project_id", projectId), SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize)]);
        var total = sets[1].FirstOrDefault()?.Int("total") ?? sets[0].Count;
        return Json(new { milestones = sets[0].Select(r => r.ToJson()).ToList(), pagination = PaginationMeta(page, pageSize, total) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Create()
    {
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();
        var projectId = body.String(Fields.ProjectId);
        if (!Js.IsUuid(projectId)) return Error("معرّف المشروع غير صالح.", 400);
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Project, projectId);
        if (denied is not null) return denied;

        var project = await db.FirstAsync("SELECT total_weeks FROM projects WHERE id = @project_id", [SqlDb.Guid(Fields.ParamProjectId, projectId)]);
        var totalWeeks = project?.Int("total_weeks") ?? 0;
        if (totalWeeks < 1) return Error("المشروع غير موجود أو مدته غير محددة.", 404);

        var (value, errors) = ProjectRules.ValidateWorkItem(body, 1, totalWeeks);
        if (value is null) return Json(new { error = "تحقق من بيانات المهمة الرئيسية.", errors }, 400);

        var inserted = await db.FirstAsync(
            "INSERT INTO milestones (project_id, name, week_start, week_end, color) OUTPUT INSERTED.id VALUES (@project_id, @name, @week_start, @week_end, @color)",
            [SqlDb.Guid(Fields.ParamProjectId, projectId), SqlDb.Text("@name", value.Name, 200), SqlDb.Int("@week_start", value.WeekStart),
             SqlDb.Int("@week_end", value.WeekEnd), SqlDb.Text("@color", ProjectRules.PhaseColor(body.String("color")), 20)]);
        await audit.WriteAsync(Me.Id, "create", "milestone", inserted?.Str("id"), projectId,
            new { name = value.Name, week_start = value.WeekStart, week_end = value.WeekEnd });
        return Json(new { success = true });
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id)
    {
        if (!Js.IsUuid(id)) return Error("معرّف المهمة الرئيسية غير صالح.", 400);
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Milestone, id);
        if (denied is not null) return denied;
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        var row = await db.FirstAsync(
            """
            SELECT m.project_id, m.name, m.week_start, m.week_end, p.total_weeks,
              (SELECT MIN(t.week_start) FROM tasks t WHERE t.milestone_id = m.id) AS min_task_week,
              (SELECT MAX(t.week_end) FROM tasks t WHERE t.milestone_id = m.id) AS max_task_week
            FROM milestones m JOIN projects p ON p.id = m.project_id WHERE m.id = @id
            """, [SqlDb.Guid("@id", id)]);
        if (row is null) return Error("المهمة الرئيسية غير موجودة.", 404);

        var (value, errors) = ProjectRules.ValidateWorkItem(body, 1, row.Int("total_weeks") ?? 0);
        if (value is null) return Json(new { error = "تحقق من بيانات المهمة الرئيسية.", errors }, 400);
        var minTask = row.Int("min_task_week");
        var maxTask = row.Int("max_task_week");
        if ((minTask is not null && minTask < value.WeekStart) || (maxTask is not null && maxTask > value.WeekEnd))
            return Error("لا يمكن تقليص نطاق المهمة الرئيسية قبل تعديل مهامها.", 409);

        await db.ExecuteAsync(
            "UPDATE milestones SET name=@name, week_start=@week_start, week_end=@week_end, color=@color WHERE id=@id",
            [SqlDb.Guid("@id", id), SqlDb.Text("@name", value.Name, 200), SqlDb.Int("@week_start", value.WeekStart),
             SqlDb.Int("@week_end", value.WeekEnd), SqlDb.Text("@color", ProjectRules.PhaseColor(body.String("color")), 20)]);
        await audit.WriteAsync(Me.Id, "update", "milestone", id, row.Str(Fields.ProjectId), new
        {
            from = new { name = row.Str("name"), week_start = row.Int("week_start"), week_end = row.Int("week_end") },
            to = new { name = value.Name, week_start = value.WeekStart, week_end = value.WeekEnd },
        });
        return Json(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Milestone, id);
        if (denied is not null) return denied;
        var existing = await db.FirstAsync("SELECT project_id, name FROM milestones WHERE id = @id", [SqlDb.Guid("@id", id)]);
        await db.ExecuteAsync(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE risks_issues SET milestone_id = NULL WHERE milestone_id = @id;
            UPDATE project_updates SET current_milestone_id = NULL WHERE current_milestone_id = @id;
            UPDATE project_updates SET next_milestone_id = NULL WHERE next_milestone_id = @id;
            DELETE FROM milestones WHERE id = @id;
            COMMIT TRANSACTION;
            """, [SqlDb.Guid("@id", id)]);
        await audit.WriteAsync(Me.Id, "delete", "milestone", id, existing?.Str(Fields.ProjectId), new { name = existing?.Str("name") });
        return Json(new { success = true });
    }
}

/// <summary>Sub-tasks under a main task, with their progress.</summary>
[Route("api/tasks")]
public sealed class TasksController(SqlDb db, ProjectAccess access, AuditService audit, ProjectCompletionWatcher completion) : ApiControllerBase
{
    private const string StatusFromProgress = "status = CASE WHEN @progress = 100 THEN 'completed' WHEN @progress > 0 THEN 'in_progress' ELSE 'pending' END";

    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var projectId = Query(Fields.ProjectId);
        if (!Js.IsUuid(projectId)) return Error("معرّف المشروع مطلوب وصحيح.", 400);
        var (page, pageSize, offset) = Paging(100, 300);
        var sets = await db.QueryMultiAsync(
            """
            SELECT t.* FROM tasks t
            JOIN milestones m ON t.milestone_id = m.id
            WHERE m.project_id = @project_id
            ORDER BY t.week_start
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM tasks t
            JOIN milestones m ON t.milestone_id = m.id
            WHERE m.project_id = @project_id;
            """, [SqlDb.Guid("@project_id", projectId), SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize)]);
        var total = sets[1].FirstOrDefault()?.Int("total") ?? sets[0].Count;
        return Json(new { tasks = sets[0].Select(r => r.ToJson()).ToList(), pagination = PaginationMeta(page, pageSize, total) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Create()
    {
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();
        var milestoneId = body.String("milestone_id");
        if (!Js.IsUuid(milestoneId)) return Error("معرّف المهمة الرئيسية غير صالح.", 400);
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Milestone, milestoneId);
        if (denied is not null) return denied;

        var parent = await db.FirstAsync("SELECT project_id, week_start, week_end FROM milestones WHERE id = @milestone_id", [SqlDb.Guid("@milestone_id", milestoneId)]);
        if (parent is null) return Error("المهمة الرئيسية غير موجودة.", 404);
        var (value, errors) = ProjectRules.ValidateWorkItem(body, parent.Int("week_start") ?? 0, parent.Int("week_end") ?? 0);
        if (value is null) return Json(new { error = "تحقق من بيانات المهمة.", errors }, 400);

        var inserted = await db.FirstAsync("INSERT INTO tasks (milestone_id, name, week_start, week_end) OUTPUT INSERTED.id VALUES (@milestone_id, @name, @week_start, @week_end)",
            [SqlDb.Guid("@milestone_id", milestoneId), SqlDb.Text("@name", value.Name, 200), SqlDb.Int("@week_start", value.WeekStart), SqlDb.Int("@week_end", value.WeekEnd)]);
        await audit.WriteAsync(Me.Id, "create", "task", inserted?.Str("id"), parent.Str(Fields.ProjectId),
            new { name = value.Name, milestone_id = milestoneId, week_start = value.WeekStart, week_end = value.WeekEnd });
        return Json(new { success = true });
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id)
    {
        if (!Js.IsUuid(id)) return Error("معرّف المهمة غير صالح.", 400);
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Task, id);
        if (denied is not null) return denied;
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        var row = await db.FirstAsync(
            """
            SELECT t.name, t.week_start, t.week_end, t.progress, m.project_id, m.week_start AS parent_start, m.week_end AS parent_end
            FROM tasks t JOIN milestones m ON m.id = t.milestone_id WHERE t.id = @id
            """, [SqlDb.Guid("@id", id)]);
        if (row is null) return Error("المهمة غير موجودة.", 404);

        var progress = ProjectRules.ValidateProgress(body.Raw("progress"));
        if (progress is null)
            return Json(new { error = "نسبة الإنجاز يجب أن تكون رقمًا صحيحًا من 0 إلى 100.", errors = new { progress = "قيمة غير صالحة." } }, 400);

        if (body.Has("name"))
        {
            var (value, errors) = ProjectRules.ValidateWorkItem(body, row.Int("parent_start") ?? 0, row.Int("parent_end") ?? 0);
            if (value is null) return Json(new { error = "تحقق من بيانات المهمة.", errors }, 400);
            await db.ExecuteAsync(
                $"UPDATE tasks SET name = @name, week_start = @week_start, week_end = @week_end, progress = @progress, {StatusFromProgress}, updated_at = GETDATE() WHERE id = @id",
                [SqlDb.Guid("@id", id), SqlDb.Text("@name", value.Name, 200), SqlDb.Int("@week_start", value.WeekStart),
                 SqlDb.Int("@week_end", value.WeekEnd), SqlDb.Int("@progress", progress)]);
        }
        else
        {
            await db.ExecuteAsync($"UPDATE tasks SET progress = @progress, {StatusFromProgress}, updated_at = GETDATE() WHERE id = @id",
                [SqlDb.Guid("@id", id), SqlDb.Int("@progress", progress)]);
        }
        await audit.WriteAsync(Me.Id, "update", "task", id, row.Str(Fields.ProjectId), new
        {
            name = body.Has("name") ? body.String("name")?.Trim() : row.Str("name"),
            progress_from = row.Int("progress"), progress_to = progress,
        });
        completion.QueueCheck(row.Str(Fields.ProjectId));
        return Json(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Task, id);
        if (denied is not null) return denied;
        var existing = await db.FirstAsync("SELECT t.name, m.project_id FROM tasks t JOIN milestones m ON m.id = t.milestone_id WHERE t.id = @id", [SqlDb.Guid("@id", id)]);
        await db.ExecuteAsync("DELETE FROM tasks WHERE id = @id", [SqlDb.Guid("@id", id)]);
        await audit.WriteAsync(Me.Id, "delete", "task", id, existing?.Str(Fields.ProjectId), new { name = existing?.Str("name") });
        completion.QueueCheck(existing?.Str(Fields.ProjectId));
        return Json(new { success = true });
    }
}

/// <summary>Phase details and support requests written on a milestone.</summary>
[Route("api/notes")]
public sealed class NotesController(SqlDb db, ProjectAccess access, AuditService audit) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery(Name = Fields.ProjectId)] string? projectId)
    {
        var (page, pageSize, offset) = Paging(100, 300);
        var sets = await db.QueryMultiAsync(
            """
            SELECT n.* FROM phase_notes n
            JOIN milestones m ON n.milestone_id = m.id
            WHERE m.project_id = @project_id
            ORDER BY n.display_order
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM phase_notes n
            JOIN milestones m ON n.milestone_id = m.id
            WHERE m.project_id = @project_id;
            """, [SqlDb.Guid("@project_id", string.IsNullOrEmpty(projectId) ? null : projectId), SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize)]);
        var total = sets[1].FirstOrDefault()?.Int("total") ?? sets[0].Count;
        return Json(new { notes = sets[0].Select(r => r.ToJson()).ToList(), pagination = PaginationMeta(page, pageSize, total) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Create()
    {
        var body = await JsonBody.ReadOrThrowAsync(Request);
        var milestoneId = body.String("milestone_id");
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Milestone, milestoneId);
        if (denied is not null) return denied;

        var content = body.String("content")?.Trim();
        if (string.IsNullOrEmpty(content)) return ApiResults.ValidationFailed(new() { ["content"] = "نص الملاحظة مطلوب" });
        if (content.Length > 4000) return ApiResults.ValidationFailed(new() { ["content"] = "الحد الأقصى 4000 حرف" });
        var type = body.String("type");
        if (type is not ("detail" or "support")) return ApiResults.ValidationFailed(new() { ["type"] = "نوع الملاحظة غير صالح (تفاصيل أو طلب دعم)" });

        var inserted = await db.FirstAsync("INSERT INTO phase_notes (milestone_id, type, content) OUTPUT INSERTED.id VALUES (@milestone_id, @type, @content)",
            [SqlDb.Guid("@milestone_id", milestoneId), SqlDb.Text("@type", type), SqlDb.Text("@content", content)]);
        await audit.WriteAsync(Me.Id, "create", "note", inserted?.Str("id"), await access.ResolveProjectIdAsync(ProjectScopeKind.Milestone, milestoneId!),
            new { type, milestone_id = milestoneId });
        return Json(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var denied = await access.RequireWriteAsync(Me, ProjectScopeKind.Note, id);
        if (denied is not null) return denied;
        var projectId = await access.ResolveProjectIdAsync(ProjectScopeKind.Note, id);
        await db.ExecuteAsync("DELETE FROM phase_notes WHERE id = @id", [SqlDb.Guid("@id", id)]);
        await audit.WriteAsync(Me.Id, "delete", "note", id, projectId);
        return Json(new { success = true });
    }
}
