using System.Text.Json;
using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace ProjectTracker.Web.Services;

/// <summary>Audit trail (table audit_log). A failed audit write is logged and never fails the operation itself.</summary>
public sealed class AuditService(SqlDb db, ILogger<AuditService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string? UuidOrNull(string? value) => Js.IsUuid(value) ? value : null;

    public async Task WriteAsync(string? userId, string action, string entityType, string? entityId = null, string? projectId = null, object? details = null)
    {
        try
        {
            string? text = details switch
            {
                null => null,
                string s => s,
                _ => JsonSerializer.Serialize(details, JsonOptions),
            };
            await db.ExecuteAsync(
                """
                INSERT INTO audit_log (user_id, action, entity_type, entity_id, project_id, details)
                VALUES (@user_id, @action, @entity_type, @entity_id, @project_id, @details)
                """,
                [
                    SqlDb.Guid("@user_id", UuidOrNull(userId)),
                    SqlDb.Text("@action", action, 20),
                    SqlDb.Text("@entity_type", entityType.Length > 50 ? entityType[..50] : entityType, 50),
                    SqlDb.Guid("@entity_id", UuidOrNull(entityId)),
                    SqlDb.Guid("@project_id", UuidOrNull(projectId)),
                    SqlDb.Text("@details", text),
                ]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record an operation in the audit log");
        }
    }
}

/// <summary>What a write request targets, to find the project it belongs to.</summary>
public enum ProjectScopeKind { Project, Milestone, Task, Note, Risk, Action }

/// <summary>Object-level authorization: a project manager edits only projects assigned to them; the admin edits any (port of lib/apiAuth.ts).</summary>
public sealed class ProjectAccess(SqlDb db)
{
    private static string LookupSql(ProjectScopeKind kind) => kind switch
    {
        ProjectScopeKind.Project => "SELECT id AS project_id FROM projects WHERE id = @id",
        ProjectScopeKind.Milestone => "SELECT project_id FROM milestones WHERE id = @id",
        ProjectScopeKind.Task => "SELECT m.project_id FROM tasks t JOIN milestones m ON m.id = t.milestone_id WHERE t.id = @id",
        ProjectScopeKind.Note => "SELECT m.project_id FROM phase_notes n JOIN milestones m ON m.id = n.milestone_id WHERE n.id = @id",
        ProjectScopeKind.Risk => "SELECT project_id FROM risks_issues WHERE id = @id",
        _ => "SELECT project_id FROM meeting_actions WHERE id = @id",
    };

    public async Task<string?> ResolveProjectIdAsync(ProjectScopeKind kind, string id)
    {
        var row = await db.FirstAsync(LookupSql(kind), [SqlDb.Guid("@id", id)]);
        return row?.Str("project_id");
    }

    public async Task<bool> IsAssignedToProjectAsync(string userId, string projectId)
    {
        if (!Js.IsUuid(userId) || !Js.IsUuid(projectId)) return false;
        var row = await db.FirstAsync("SELECT TOP 1 1 AS ok FROM assignments WHERE project_id = @project_id AND user_id = @user_id",
            [SqlDb.Guid("@project_id", projectId), SqlDb.Guid("@user_id", userId)]);
        return row is not null;
    }

    /// <summary>Null when the write is allowed, otherwise the ready-made error response.</summary>
    public async Task<IActionResult?> RequireWriteAsync(SessionPayload user, ProjectScopeKind kind, string? id)
    {
        if (!Js.IsUuid(id)) return ApiResults.Error("معرّف غير صالح", 400);

        var projectId = await ResolveProjectIdAsync(kind, id!);
        if (projectId is null) return ApiResults.Error("العنصر غير موجود أو حُذف", 404);
        if (user.Role == "admin") return null;
        if (user.Role != "pm") return ApiResults.Error("صلاحيتك للعرض فقط، فلا يمكنك تعديل بيانات المشروع.", 403);

        return await IsAssignedToProjectAsync(user.Id, projectId)
            ? null
            : ApiResults.Error("هذا المشروع غير مُسند إليك، فلا يمكنك تعديل بياناته.", 403);
    }

    /// <summary>
    /// The referenced milestones must belong to the project and the owner must be a live employee.
    /// Returns per-field errors (empty = fine).
    /// </summary>
    public async Task<Dictionary<string, string>> CheckReferencesAsync(
        string projectId, IEnumerable<KeyValuePair<string, string?>>? milestones = null, string? ownerField = null, string? ownerId = null)
    {
        var errors = new Dictionary<string, string>();

        var entries = (milestones ?? []).Where(e => e.Value is not null).Select(e => (Field: e.Key, Id: e.Value!)).ToList();
        if (entries.Count > 0)
        {
            var parameters = new List<Microsoft.Data.SqlClient.SqlParameter> { SqlDb.Guid("@pid", projectId) };
            var names = entries.Select((e, i) =>
            {
                parameters.Add(SqlDb.Guid($"@m{i}", e.Id));
                return $"@m{i}";
            });
            var rows = await db.QueryAsync($"SELECT id FROM milestones WHERE project_id = @pid AND id IN ({string.Join(", ", names)})", parameters);
            var found = rows.Select(r => r.Str("id")!.ToLowerInvariant()).ToHashSet();
            foreach (var (field, id) in entries)
                if (!found.Contains(id.ToLowerInvariant())) errors[field] = "المرحلة لا تتبع هذا المشروع";
        }

        if (ownerField is not null && ownerId is not null)
        {
            var owner = await db.FirstAsync("SELECT TOP 1 1 AS ok FROM profiles WHERE id = @owner AND ISNULL(status, 'active') <> 'deleted'",
                [SqlDb.Guid("@owner", ownerId)]);
            if (owner is null) errors[ownerField] = "المسؤول غير موجود";
        }
        return errors;
    }
}
