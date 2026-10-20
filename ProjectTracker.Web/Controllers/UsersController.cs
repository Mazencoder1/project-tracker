using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Middleware;
using ProjectTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectTracker.Web.Controllers;

[Route("api/users")]
public sealed class UsersController(
    SqlDb db,
    SessionService sessions,
    AuditService audit,
    IServiceScopeFactory scopeFactory,
    ILogger<UsersController> logger) : ApiControllerBase
{
    private static readonly string[] AllowedStatuses = [Fields.Active, "disabled"];

    private static readonly Dictionary<string, string> UserSortColumns = new()
    {
        ["name"] = Fields.FullName,
        [Fields.EmployeeNumber] = Fields.EmployeeNumber,
        [Fields.JobTitle] = Fields.JobTitle,
        [Fields.Email] = Fields.Email,
        ["role"] = "CASE role WHEN 'pm' THEN 0 WHEN 'admin' THEN 1 ELSE 2 END",
        ["dept"] = Fields.Department,
        [Fields.Status] = "CASE ISNULL(status, 'active') WHEN 'disabled' THEN 0 ELSE 1 END",
        ["created"] = "created_at",
    };

    /// <summary>Employee list. The admin sees the full record; the deputy sees only what is needed to pick an assignee.</summary>
    [Authorize(Policy = TrackerAuth.AdminOrDeputyPolicy)]
    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var role = Query("role");
        var status = Query(Fields.Status);
        if (role is not null && !ProjectRules.UserRoles.Contains(role)) return Error("دور غير معروف", 400);
        if (status is not null && !AllowedStatuses.Contains(status)) return Error("حالة غير معروفة", 400);
        var search = SearchPattern();
        var (page, pageSize, offset) = Paging(20, 100);
        var orderBy = OrderBy(UserSortColumns, "created_at DESC");
        var fullRow = Me.Role == Fields.Admin;
        // The deputy never sees the employee number, so it cannot be searched by it either.
        var searchable = fullRow
            ? "full_name LIKE @q ESCAPE '\\' OR email LIKE @q ESCAPE '\\' OR employee_number LIKE @q ESCAPE '\\' OR job_title LIKE @q ESCAPE '\\' OR department LIKE @q ESCAPE '\\'"
            : "full_name LIKE @q ESCAPE '\\' OR email LIKE @q ESCAPE '\\' OR job_title LIKE @q ESCAPE '\\' OR department LIKE @q ESCAPE '\\'";
        const string scope = "ISNULL(status, 'active') <> 'deleted'";
        var filters = $"(@role IS NULL OR role = @role) AND (@status IS NULL OR ISNULL(status, 'active') = @status) AND (@q IS NULL OR {searchable})";

        var sets = await db.QueryMultiAsync(
            $"""
            SELECT id, full_name, email, role, department, status,
                   employee_number, job_title, mobile_no,
                   main_department, section_name, extension_number,
                   manager_name, manager_number, authorized_manager_name,
                   authorized_manager_number, directory_synced_at
            FROM profiles
            WHERE {scope} AND {filters}
            ORDER BY {orderBy}, id
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

            SELECT COUNT(*) AS total FROM profiles WHERE {scope} AND {filters};
            SELECT COUNT(*) AS allTotal FROM profiles WHERE {scope};
            """,
            [
                SqlDb.Text("@role", role, 20), SqlDb.Text("@status", status, 20), SqlDb.Text("@q", search, 210),
                SqlDb.Int("@offset", offset), SqlDb.Int("@pageSize", pageSize),
            ]);
        var rows = sets[0];
        var total = sets[1].FirstOrDefault()?.Int("total") ?? rows.Count;
        var allTotal = sets[2].FirstOrDefault()?.Int("allTotal") ?? total;

        var myId = Me.Id.ToLowerInvariant();
        var users = rows.Select(row =>
        {
            var json = row.ToJson();
            json["is_self"] = string.Equals(row.Str("id"), myId, StringComparison.OrdinalIgnoreCase);
            if (fullRow) return json;
            return new Dictionary<string, object?>
            {
                ["id"] = json["id"], [Fields.FullName] = json[Fields.FullName], [Fields.Email] = json[Fields.Email], ["role"] = json["role"],
                [Fields.Department] = json[Fields.Department], [Fields.Status] = json[Fields.Status], [Fields.JobTitle] = json[Fields.JobTitle], ["is_self"] = json["is_self"],
            };
        }).ToList();
        return Json(new { users, allTotal, pagination = PaginationMeta(page, pageSize, total) });
    }

    /// <summary>Creates an account from the data typed by the administrator.</summary>
    [Authorize(Policy = TrackerAuth.AdminOnlyPolicy)]
    [HttpPost("")]
    public async Task<IActionResult> Create()
    {
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        var employeeNumber = EmployeeNumberFrom(body.Raw(Fields.EmployeeNumber));
        var employeeNumberError = EmployeeNumbers.EmployeeNumberProblem(employeeNumber);
        if (employeeNumberError is not null)
            return Json(new { error = employeeNumberError, field = Fields.EmployeeNumber, errors = new { employee_number = employeeNumberError } }, 400);

        string Field(string key) => body.String(key)?.Trim() ?? "";
        var employee = new EmployeeRecord(
            employeeNumber, Field(Fields.FullName), Field(Fields.JobTitle), Field(Fields.Department), Field("main_department"),
            Field("section_name"), Field(Fields.Email), Field("mobile_no"), Field("extension_number"),
            Field("manager_name"), Field("manager_number"), Field("authorized_manager_name"), Field("authorized_manager_number"));

        var (value, errors) = ProjectRules.ValidateUserProfile(
            new ProjectRules.UserProfileFields(
                employee.FullName, employee.Email, employee.Department is { Length: > 0 } d ? d : employee.MainDepartment, body.Raw("role"),
                HasFullName: true, HasEmail: true, HasDepartment: true, HasRole: true),
            partial: false);
        var pwProblem = ProjectRules.PasswordProblem(body.Raw(Fields.PasswordHash));
        if (pwProblem is not null) errors[Fields.PasswordHash] = pwProblem;
        if (errors.Count > 0)
        {
            var first = errors.First();
            return Json(new { error = first.Value, errors, field = first.Key }, 400);
        }

        var duplicate = await db.FirstAsync(
            """
            SELECT TOP 1
              CASE
                WHEN employee_number = @employee_number THEN 'employee_number'
                ELSE 'email'
              END AS duplicate_field
            FROM profiles
            WHERE (
                LOWER(LTRIM(RTRIM(email))) = LOWER(@email)
                AND ISNULL(status, 'active') <> 'deleted'
              )
              OR employee_number = @employee_number
            """,
            [SqlDb.Text("@email", value[Fields.Email]), SqlDb.Text("@employee_number", employee.EmployeeNumber, 50)]);
        if (duplicate is not null)
        {
            var field = duplicate.Str("duplicate_field") is { Length: > 0 } f ? f : Fields.EmployeeNumber;
            var message = field == Fields.EmployeeNumber ? "الرقم الوظيفي مرتبط بحساب موجود."
                : "البريد الإلكتروني مسجّل لموظف آخر.";
            return Json(new { error = $"{message} فعّل الحساب الموجود أو راجع بياناته.", field, errors = new Dictionary<string, string> { [field] = message } }, 409);
        }

        var passwordHash = PasswordHasher.Hash(body.Raw(Fields.PasswordHash)!.ToString()!);
        var created = await db.FirstAsync(
            """
            INSERT INTO profiles (
              full_name, email, role, department, password_hash,
              employee_number, job_title, mobile_no,
              main_department, section_name, extension_number,
              manager_name, manager_number, authorized_manager_name,
              authorized_manager_number, directory_synced_at
            ) OUTPUT INSERTED.id VALUES (
              @full_name, @email, @role, @department, @password_hash,
              @employee_number, @job_title, @mobile_no,
              @main_department, @section_name, @extension_number,
              @manager_name, @manager_number, @authorized_manager_name,
              @authorized_manager_number, NULL
            )
            """,
            [
                SqlDb.Text("@full_name", value[Fields.FullName]), SqlDb.Text("@email", value[Fields.Email]), SqlDb.Text("@role", value["role"]),
                SqlDb.Text("@department", value.GetValueOrDefault(Fields.Department) ?? ""), SqlDb.Text("@password_hash", passwordHash, 255),
                SqlDb.Text("@employee_number", employee.EmployeeNumber, 50),
                SqlDb.Text("@job_title", NullIfEmpty(employee.JobTitle), 200), SqlDb.Text("@mobile_no", NullIfEmpty(employee.MobileNo), 30),
                SqlDb.Text("@main_department", NullIfEmpty(employee.MainDepartment), 200), SqlDb.Text("@section_name", NullIfEmpty(employee.Section), 200),
                SqlDb.Text("@extension_number", NullIfEmpty(employee.Extension), 20), SqlDb.Text("@manager_name", NullIfEmpty(employee.ManagerName), 200),
                SqlDb.Text("@manager_number", NullIfEmpty(employee.ManagerNumber), 50),
                SqlDb.Text("@authorized_manager_name", NullIfEmpty(employee.AuthorizedManagerName), 200),
                SqlDb.Text("@authorized_manager_number", NullIfEmpty(employee.AuthorizedManagerNumber), 50),
            ]);

        await audit.WriteAsync(Me.Id, "create", "user", created?.Str("id"), null, new
        {
            full_name = value[Fields.FullName], employee_number = employee.EmployeeNumber, email = value[Fields.Email], role = value["role"],
            department = value.GetValueOrDefault(Fields.Department),
        });

        // Email/SMS go out after the response, not before it — the internal mail and SMS
        // gateways can each take several seconds (or the full timeout) to answer, and the
        // account is already created; there is no reason to make the admin wait on delivery.
        var notifyEmployee = employee with
        {
            FullName = value[Fields.FullName], Email = value[Fields.Email], Department = value.GetValueOrDefault(Fields.Department) ?? "",
        };
        _ = SendAccountCreatedInBackgroundAsync(notifyEmployee);
        return Json(new { success = true, notifications = (object?)null }, 201);
    }

    /// <summary>Fire-and-forget: runs in its own DI scope since the request's scope is gone once the response is sent.</summary>
    private async Task SendAccountCreatedInBackgroundAsync(EmployeeRecord employee)
    {
        using var scope = scopeFactory.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<AccountNotifications>().SendAccountCreatedAsync(employee);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[users] account-created notification failed for {Email}", employee.Email);
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Employee number from a JSON string or number (Arabic digits normalised); anything else reads as empty.</summary>
    internal static string EmployeeNumberFrom(object? raw)
    {
        if (raw is not (string or long or int or double)) return "";
        var text = raw is double d ? Js.NumberToString(d) : raw.ToString();
        return EmployeeNumbers.Normalize(text);
    }

    /// <summary>Request-level checks of an employee edit; returns the validated profile fields or the error response.</summary>
    private static (IActionResult? Failure, Dictionary<string, string> Profile) ValidateUpdate(JsonBody body)
    {
        var statusRaw = body.Raw(Fields.Status);
        if (statusRaw is "deleted") return (Error("لحذف الموظف نهائيًا استخدم طلب DELETE.", 400), []);
        if (body.Has(Fields.Status) && !AllowedStatuses.Contains(statusRaw?.ToString() ?? "null"))
            return (Error("حالة الموظف غير صحيحة", 400), []);

        var profile = ProjectRules.ValidateUserProfile(
            new ProjectRules.UserProfileFields(
                body.Raw(Fields.FullName), null, body.Raw(Fields.Department), body.Raw("role"),
                HasFullName: body.Has(Fields.FullName), HasEmail: false, HasDepartment: body.Has(Fields.Department), HasRole: body.Has("role")),
            partial: true);
        var errors = new Dictionary<string, string>(profile.Errors);
        if (body.Has(Fields.Password) && ProjectRules.PasswordProblem(body.Raw(Fields.Password)) is { } problem) errors[Fields.Password] = problem;
        if (errors.Count == 0) return (null, profile.Value);

        var first = errors.First();
        return (Json(new { error = first.Value, errors, field = first.Key }, 400), []);
    }

    /// <summary>column → (value, size): only the fields that really differ from the stored employee.</summary>
    private static Dictionary<string, (string? Value, int Size)> CollectChanges(JsonBody body, Dictionary<string, string> profile, Row found)
    {
        var statusRaw = body.Raw(Fields.Status);
        var targetDepartment = found.Str(Fields.Department);
        var changes = new Dictionary<string, (string? Value, int Size)>();
        if (body.Has(Fields.Status) && statusRaw?.ToString() != found.Str(Fields.Status)) changes[Fields.Status] = (statusRaw?.ToString(), 20);
        if (profile.TryGetValue(Fields.FullName, out var newName) && newName != found.Str(Fields.FullName)) changes[Fields.FullName] = (newName, 100);
        if (profile.TryGetValue(Fields.Department, out var newDept) && newDept != (targetDepartment ?? "")) changes[Fields.Department] = (newDept, 100);
        if (profile.TryGetValue("role", out var newRole) && newRole != found.Str("role")) changes["role"] = (newRole, 20);
        if (body.Has(Fields.Password)) changes[Fields.PasswordHash] = (PasswordHasher.Hash(body.Raw(Fields.Password)!.ToString()!), 255);
        return changes;
    }

    /// <summary>Nobody may suspend or demote their own account, and the system must always keep one active administrator.</summary>
    private async Task<IActionResult?> CheckAdminSafetyAsync(string id, Row found, Dictionary<string, (string? Value, int Size)> changes)
    {
        var targetRole = found.Str("role")!;
        var targetStatus = found.Str(Fields.Status)!;
        var nextStatus = changes.TryGetValue(Fields.Status, out var st) ? st.Value : targetStatus;
        var nextRole = changes.TryGetValue("role", out var rl) ? rl.Value : targetRole;
        var isSelf = string.Equals(found.Str("id"), Me.Id, StringComparison.OrdinalIgnoreCase);
        if (isSelf && nextStatus != Fields.Active) return Error("لا يمكنك إيقاف حسابك أو حذفه بنفسك. اطلب ذلك من مدير نظام آخر.", 400);
        if (isSelf && nextRole != targetRole) return Error("لا يمكنك تغيير دور حسابك بنفسك. اطلب ذلك من مدير نظام آخر.", 400);

        var losesAdmin = targetRole == Fields.Admin && targetStatus == Fields.Active && (nextStatus != Fields.Active || nextRole != Fields.Admin);
        if (!losesAdmin) return null;
        var others = await db.FirstAsync("SELECT COUNT(*) AS n FROM profiles WHERE role = 'admin' AND ISNULL(status, 'active') = 'active' AND id <> @id",
            [SqlDb.Guid("@id", id)]);
        return (others?.Int("n") ?? 0) == 0
            ? Error("هذا آخر مدير نظام نشط. أضف مدير نظام آخر أولًا حتى لا تُغلق الإدارة.", 409)
            : null;
    }

    [Authorize(Policy = TrackerAuth.AdminOnlyPolicy)]
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id)
    {
        if (!Js.IsUuid(id)) return Error("معرّف الموظف غير صالح", 400);
        var body = await JsonBody.ReadAsync(Request);
        if (body is null) return ApiResults.BadBody();

        var (failure, profile) = ValidateUpdate(body);
        if (failure is not null) return failure;

        var found = await db.FirstAsync(
            """
            SELECT id, full_name, department, role, ISNULL(status, 'active') AS status
            FROM profiles WHERE id = @id AND ISNULL(status, 'active') <> 'deleted'
            """, [SqlDb.Guid("@id", id)]);
        if (found is null) return Error("الموظف غير موجود أو حُذف", 404);

        var changes = CollectChanges(body, profile, found);
        if (changes.Count == 0) return Json(new { success = true, unchanged = true });
        if (await CheckAdminSafetyAsync(id, found, changes) is { } unsafeChange) return unsafeChange;

        var parameters = new List<SqlParameter> { SqlDb.Guid("@id", id) };
        var sets = new List<string>();
        foreach (var (column, change) in changes)
        {
            parameters.Add(SqlDb.Text($"@set_{column}", change.Value, change.Size));
            sets.Add($"{column} = @set_{column}");
        }
        await db.ExecuteAsync($"UPDATE profiles SET {string.Join(", ", sets)} WHERE id = @id", parameters);

        // Suspension, role change and password reset end the account's sessions immediately.
        if (changes.ContainsKey(Fields.Status) || changes.ContainsKey("role") || changes.ContainsKey(Fields.PasswordHash))
            await sessions.RevokeUserSessionsAsync(id);

        var before = new Dictionary<string, string?>
        {
            [Fields.Status] = found.Str(Fields.Status), [Fields.FullName] = found.Str(Fields.FullName),
            [Fields.Department] = found.Str(Fields.Department), ["role"] = found.Str("role"),
        };
        var details = new Dictionary<string, object?>();
        foreach (var (column, change) in changes)
            details[column] = column == Fields.PasswordHash ? "إعادة تعيين" : new { from = before.GetValueOrDefault(column), to = change.Value };
        await audit.WriteAsync(Me.Id, "update", "user", id, null, details);

        return Json(new { success = true });
    }

    /// <summary>Permanent deletion of an employee and everything that points at them, in one transaction.</summary>
    [Authorize(Policy = TrackerAuth.AdminOnlyPolicy)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!Js.IsUuid(id)) return Error("معرّف الموظف غير صالح", 400);
        if (string.Equals(id, Me.Id, StringComparison.OrdinalIgnoreCase)) return Error("لا يمكنك حذف حسابك بنفسك. اطلب ذلك من مدير نظام آخر.", 400);

        Row? target;
        await using var tx = await db.Context.Database.BeginTransactionAsync();
        try
        {
            var sets = await db.QueryMultiAsync(
                """
                SELECT id, role, ISNULL(status, 'active') AS status, full_name, employee_number, email
                FROM profiles WITH (UPDLOCK, HOLDLOCK)
                WHERE id = @id;

                SELECT COUNT(*) AS n
                FROM profiles WITH (UPDLOCK, HOLDLOCK)
                WHERE role = 'admin' AND ISNULL(status, 'active') = 'active' AND id <> @id;
                """, [SqlDb.Guid("@id", id)]);
            target = sets[0].FirstOrDefault();
            if (target is null)
            {
                await tx.RollbackAsync();
                return Error("الموظف غير موجود أو حُذف مسبقًا", 404);
            }
            if (target.Str("role") == Fields.Admin && target.Str(Fields.Status) == Fields.Active && (sets[1].FirstOrDefault()?.Int("n") ?? 0) == 0)
            {
                await tx.RollbackAsync();
                return Error("هذا آخر مدير نظام نشط. أضف مدير نظام آخر أولًا حتى لا تُغلق الإدارة.", 409);
            }

            await db.ExecuteAsync(
                """
                UPDATE app_settings SET updated_by = NULL WHERE updated_by = @id;
                UPDATE project_updates SET updated_by = NULL WHERE updated_by = @id;
                UPDATE risks_issues SET owner_id = NULL WHERE owner_id = @id;
                UPDATE risks_issues SET created_by = NULL WHERE created_by = @id;
                UPDATE meeting_actions SET owner_id = NULL WHERE owner_id = @id;
                UPDATE meeting_actions SET created_by = NULL WHERE created_by = @id;

                -- سجل التدقيق يبقى بعد حذف الموظف (كان يُمسح كل سطر يذكره).
                DELETE FROM notification_log WHERE user_id = @id;
                IF OBJECT_ID(N'dbo.EmailNotificationLog', N'U') IS NOT NULL
                  DELETE FROM dbo.EmailNotificationLog WHERE UserId = @id;
                DELETE FROM login_otp WHERE user_id = @id;
                DELETE FROM user_sessions WHERE user_id = @id;
                DELETE FROM assignments WHERE user_id = @id;
                DELETE FROM profiles WHERE id = @id;
                """, [SqlDb.Guid("@id", id)]);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        await audit.WriteAsync(Me.Id, "delete", "user", id, null, new
        {
            hardDeleted = true, full_name = target.Str(Fields.FullName), employee_number = target.Str(Fields.EmployeeNumber),
            email = target.Str(Fields.Email), role = target.Str("role"),
        });
        return Json(new { success = true, deleted = true });
    }
}
