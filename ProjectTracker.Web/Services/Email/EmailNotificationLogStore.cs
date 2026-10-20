using ProjectTracker.Web.Data;
using ProjectTracker.Web.Models;

namespace ProjectTracker.Web.Services.Email;

/// <param name="Claimed">This caller owns the send. False means someone else already did it (or is doing it).</param>
/// <param name="Id">The log row to update when claimed.</param>
/// <param name="ExistingStatus">When not claimed: the status of the row that blocked us.</param>
public sealed record ClaimResult(bool Claimed, Guid Id, string? ExistingStatus);

public interface IEmailNotificationLog
{
    Task<ClaimResult> TryClaimAsync(string correlationKey, string templateKey, Guid? projectId, Guid? userId, string? recipientEmail, CancellationToken ct = default);
    Task MarkSentAsync(Guid id, string subject, CancellationToken ct = default);
    Task MarkFailedAsync(Guid id, string? subject, string error, bool retryable, CancellationToken ct = default);
}

/// <summary>
/// The send-once guard. A message is claimed by INSERTing its CorrelationKey (Pending); the unique index makes that
/// atomic across requests, restarts and server instances. Only a Failed row that still has attempts left (after a
/// pause) or a Pending row abandoned by a crashed sender can be claimed again; a Sent row never can.
/// </summary>
public sealed class EmailNotificationLogStore(SqlDb db) : IEmailNotificationLog
{
    public const int MaxAttempts = 3;
    public const int RetryAfterMinutes = 30;
    public const int PendingTimeoutMinutes = 10;
    private const int ErrorMax = 1000;
    private const int SubjectMax = 300;

    private static string Clip(string? value, int max)
    {
        if (value is null) return "";
        return value.Length <= max ? value : value[..max];
    }

    public async Task<ClaimResult> TryClaimAsync(string correlationKey, string templateKey, Guid? projectId, Guid? userId, string? recipientEmail, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        try
        {
            await db.ExecuteAsync(
                """
                INSERT INTO dbo.EmailNotificationLog (Id, TemplateKey, ProjectId, UserId, RecipientEmail, Status, CorrelationKey, AttemptCount, CreatedAt, UpdatedAt)
                VALUES (@id, @template, @project, @user, @email, N'Pending', @key, 1, SYSUTCDATETIME(), SYSUTCDATETIME())
                """,
                [SqlDb.Guid("@id", id.ToString()), SqlDb.Text("@template", templateKey, 50), SqlDb.Guid("@project", projectId?.ToString()), SqlDb.Guid("@user", userId?.ToString()),
                 SqlDb.Text("@email", recipientEmail is null ? null : Clip(recipientEmail, 320), 320), SqlDb.Text("@key", correlationKey, 300)], ct);
            return new ClaimResult(true, id, null);
        }
        catch (Exception ex) when (SqlDb.SqlErrorNumber(ex) is 2627 or 2601)
        {
            // Already there: take it over only if it failed with attempts left, or its sender vanished mid-send.
            var reclaimed = await db.FirstAsync(
                """
                UPDATE dbo.EmailNotificationLog
                SET Status = N'Pending', AttemptCount = AttemptCount + 1, ErrorMessage = NULL, RecipientEmail = @email, UpdatedAt = SYSUTCDATETIME()
                OUTPUT INSERTED.Id
                WHERE CorrelationKey = @key
                  AND ( (Status = N'Failed'  AND AttemptCount < @max AND UpdatedAt < DATEADD(MINUTE, -@retry, SYSUTCDATETIME()))
                     OR (Status = N'Pending' AND UpdatedAt < DATEADD(MINUTE, -@pending, SYSUTCDATETIME())) )
                """,
                [SqlDb.Text("@email", recipientEmail is null ? null : Clip(recipientEmail, 320), 320), SqlDb.Text("@key", correlationKey, 300), SqlDb.Int("@max", MaxAttempts),
                 SqlDb.Int("@retry", RetryAfterMinutes), SqlDb.Int("@pending", PendingTimeoutMinutes)], ct);
            if (reclaimed is not null) return new ClaimResult(true, Guid.Parse(reclaimed.Str("Id")!), null);

            var existing = await db.FirstAsync("SELECT Status FROM dbo.EmailNotificationLog WHERE CorrelationKey = @key", [SqlDb.Text("@key", correlationKey, 300)], ct);
            return new ClaimResult(false, Guid.Empty, existing?.Str("Status"));
        }
    }

    public Task MarkSentAsync(Guid id, string subject, CancellationToken ct = default) =>
        db.ExecuteAsync(
            """
            UPDATE dbo.EmailNotificationLog
            SET Status = N'Sent', SentAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME(), Subject = @subject, ErrorMessage = NULL
            WHERE Id = @id
            """,
            [SqlDb.Guid("@id", id.ToString()), SqlDb.Text("@subject", Clip(subject, SubjectMax), SubjectMax)], ct);

    /// <summary>A failure that will not fix itself is stored with AttemptCount = MaxAttempts, so it is never claimed again.</summary>
    public Task MarkFailedAsync(Guid id, string? subject, string error, bool retryable, CancellationToken ct = default) =>
        db.ExecuteAsync(
            """
            UPDATE dbo.EmailNotificationLog
            SET Status = N'Failed', UpdatedAt = SYSUTCDATETIME(), ErrorMessage = @error,
                Subject = ISNULL(@subject, Subject),
                AttemptCount = CASE WHEN @retryable = 1 THEN AttemptCount ELSE @max END
            WHERE Id = @id
            """,
            [SqlDb.Guid("@id", id.ToString()), SqlDb.Text("@error", Clip(error, ErrorMax), ErrorMax),
             SqlDb.Text("@subject", subject is null ? null : Clip(subject, SubjectMax), SubjectMax), SqlDb.Int("@retryable", retryable ? 1 : 0), SqlDb.Int("@max", MaxAttempts)], ct);
}
