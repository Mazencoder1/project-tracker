using ProjectTracker.Web.ViewModels;
using System.Globalization;
using ProjectTracker.Web.Data;

namespace ProjectTracker.Web.Services.Email;

public enum EmailNotifyOutcome { Sent, AlreadySent, Skipped, TemplateDisabled, TemplateMissing, Failed }

public sealed record WeeklyReminderSummary(bool Ran = false, string? SkippedReason = null, int Sent = 0, int Duplicates = 0, int Skipped = 0, int Failed = 0);

public interface IProjectEmailNotifier
{
    /// <summary>Call after the assignment row is saved. Never throws; a failure is logged and reported as an outcome.</summary>
    Task<EmailNotifyOutcome> NotifyProjectAssignedAsync(Guid assignmentId, string projectId, string userId, CancellationToken ct = default);

    /// <summary>Call after a real transition of the project status to completed. Returns how many messages were sent.</summary>
    Task<int> NotifyProjectCompletedAsync(string projectId, CancellationToken ct = default);

    /// <summary>One pass of the weekly job: does nothing unless the configured weekly window is open.</summary>
    Task<WeeklyReminderSummary> RunWeeklyRemindersAsync(CancellationToken ct = default);
}

/// <summary>
/// The notification layer between the project code and the mailer:
/// project event → this class (who, which key, which values) → template service (subject/body from the database) →
/// e-mail service (SMTP). It contains no message text; it only knows TemplateKeys and the data it hands to them.
/// Every send is claimed in EmailNotificationLog first, so it cannot happen twice.
/// </summary>
/// <summary>The four services every e-mail send goes through (template store, relay, send log, weekly-reminder settings).</summary>
public sealed record EmailPipeline(IEmailTemplateService Templates, IEmailService Email, IEmailNotificationLog Log, WeeklyReminderSettingsService ReminderSettings);

public sealed class ProjectEmailNotifier(
    SqlDb db,
    PortfolioQueries portfolio,
    EmailPipeline pipeline,
    ProjectLinkBuilder links,
    AppConfig config,
    TimeProvider clock,
    ILogger<ProjectEmailNotifier> logger) : IProjectEmailNotifier
{
    /// <summary>Completion e-mails are sent inside the PATCH request: a dead relay must not hold the administrator's screen for minutes.</summary>
    private static readonly TimeSpan CompletionBudget = TimeSpan.FromSeconds(20);

    /// <summary>One weekly pass gives up after this long (or after {ConsecutiveFailureLimit} failed sends in a row); unclaimed reminders are picked up by the next check.</summary>
    private static readonly TimeSpan WeeklyRunBudget = TimeSpan.FromMinutes(5);
    private const int ConsecutiveFailureLimit = 3;

    private sealed record MailRecipient(string Id, string FullName, string? Email, bool Active);

    private sealed record Job(string TemplateKey, string CorrelationKey, string ProjectId, MailRecipient? MailRecipient, string UserId,
        Func<MailRecipient, Task<IReadOnlyDictionary<string, string?>>> Variables);

    // ── correlation keys (the once-only rule of each message) ───────────────
    public static string AssignedKey(string projectId, string userId, Guid assignmentId) =>
        $"{EmailTemplateKeys.ProjectAssigned}:{projectId.ToLowerInvariant()}:{userId.ToLowerInvariant()}:{assignmentId:D}";

    public static string CompletedKey(string projectId, string userId) =>
        $"{EmailTemplateKeys.ProjectCompleted}:{projectId.ToLowerInvariant()}:{userId.ToLowerInvariant()}";

    public static string WeeklyKey(string projectId, string userId, int isoYear, int isoWeek) =>
        $"{EmailTemplateKeys.ProjectWeeklyReminder}:{projectId.ToLowerInvariant()}:{userId.ToLowerInvariant()}:{isoYear}:{isoWeek}";

    // ── recipients ──────────────────────────────────────────────────────────
    private static MailRecipient ToRecipient(Row r) =>
        new(r.Str("id")!, r.Str("full_name") ?? "", r.Str("email")?.Trim(), (r.Str("status") ?? "active") == "active");

    private async Task<MailRecipient?> LoadRecipientAsync(string userId)
    {
        var row = await db.FirstAsync("SELECT id, full_name, email, ISNULL(status, 'active') AS status FROM profiles WHERE id = @id", [SqlDb.Guid("@id", userId)]);
        return row is null ? null : ToRecipient(row);
    }

    private async Task<Dictionary<string, MailRecipient>> LoadRecipientsAsync(IEnumerable<string> userIds)
    {
        var ids = userIds.Select(i => i.ToLowerInvariant()).Distinct().ToHashSet();
        if (ids.Count == 0) return [];
        var rows = await db.QueryAsync("SELECT id, full_name, email, ISNULL(status, 'active') AS status FROM profiles");
        return rows.Select(ToRecipient).Where(r => ids.Contains(r.Id.ToLowerInvariant())).ToDictionary(r => r.Id.ToLowerInvariant());
    }

    // ── the one place a message is claimed, rendered, sent and recorded ─────
    private async Task<EmailNotifyOutcome> SendAsync(Job job, StoredTemplate? preloaded, CancellationToken ct)
    {
        var template = preloaded ?? await pipeline.Templates.GetForSendAsync(job.TemplateKey, ct);
        if (template is null)
        {
            logger.LogError("E-mail template {Template} does not exist in EmailTemplates — nothing was sent ({Key})", job.TemplateKey, job.CorrelationKey);
            return EmailNotifyOutcome.TemplateMissing;
        }
        if (!template.IsEnabled)
        {
            logger.LogInformation("E-mail template {Template} is disabled — nothing was sent ({Key})", job.TemplateKey, job.CorrelationKey);
            return EmailNotifyOutcome.TemplateDisabled;
        }

        var recipient = job.MailRecipient;
        var claim = await pipeline.Log.TryClaimAsync(job.CorrelationKey, job.TemplateKey, ParseGuid(job.ProjectId), ParseGuid(job.UserId), recipient?.Email, ct);
        if (!claim.Claimed)
        {
            logger.LogDebug("E-mail {Key} was already handled ({Status}) — not sent again", job.CorrelationKey, claim.ExistingStatus);
            return EmailNotifyOutcome.AlreadySent;
        }

        string? skipReason = recipient switch
        {
            null => "Skipped: the user does not exist.",
            { Active: false } => "Skipped: the user account is not active.",
            { Email: null or "" } => "Skipped: the user has no e-mail address.",
            _ when !pipeline.Email.IsValidAddress(recipient.Email) => "Skipped: the user's e-mail address is not valid.",
            _ => null,
        };
        if (skipReason is not null)
        {
            await pipeline.Log.MarkFailedAsync(claim.Id, null, skipReason, retryable: false, CancellationToken.None);
            logger.LogWarning("E-mail {Key} not sent — {Reason}", job.CorrelationKey, skipReason);
            return EmailNotifyOutcome.Skipped;
        }

        RenderedEmail rendered;
        try
        {
            rendered = pipeline.Templates.Render(template, await job.Variables(recipient!));
        }
        catch (Exception ex)
        {
            await pipeline.Log.MarkFailedAsync(claim.Id, null, $"Rendering failed: {ex.GetType().Name}: {ex.Message}", retryable: true, CancellationToken.None);
            logger.LogError(ex, "Could not render e-mail {Key}", job.CorrelationKey);
            return EmailNotifyOutcome.Failed;
        }

        var result = await pipeline.Email.SendAsync(recipient!.Email!, rendered.Subject, rendered.BodyHtml, ct);
        if (result.Success)
        {
            await pipeline.Log.MarkSentAsync(claim.Id, rendered.Subject, CancellationToken.None);
            logger.LogInformation("E-mail {Template} sent ({Key})", job.TemplateKey, job.CorrelationKey);
            return EmailNotifyOutcome.Sent;
        }

        await pipeline.Log.MarkFailedAsync(claim.Id, rendered.Subject, result.Error ?? "Unknown error", result.Retryable, CancellationToken.None);
        logger.LogError("E-mail {Template} could not be sent ({Key}): {Error}", job.TemplateKey, job.CorrelationKey, result.Error);
        return EmailNotifyOutcome.Failed;
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : null;

    // ── ProjectAssigned ─────────────────────────────────────────────────────
    public async Task<EmailNotifyOutcome> NotifyProjectAssignedAsync(Guid assignmentId, string projectId, string userId, CancellationToken ct = default)
    {
        try
        {
            var built = await portfolio.LoadBuiltProjectAsync(projectId);
            if (built is null)
            {
                logger.LogWarning("Assignment e-mail skipped: project {Project} was not found", projectId);
                return EmailNotifyOutcome.Skipped;
            }
            var recipient = await LoadRecipientAsync(userId);
            var url = links.Build(projectId);
            var job = new Job(EmailTemplateKeys.ProjectAssigned, AssignedKey(projectId, userId, assignmentId), projectId, recipient, userId,
                r => Task.FromResult(ProjectEmailVariables.Build(built.Project, r.FullName, url)));
            return await SendAsync(job, null, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The assignment e-mail failed for project {Project}, user {User}; the assignment itself is saved", projectId, userId);
            return EmailNotifyOutcome.Failed;
        }
    }

    // ── ProjectCompleted ────────────────────────────────────────────────────
    public async Task<int> NotifyProjectCompletedAsync(string projectId, CancellationToken ct = default)
    {
        var sent = 0;
        try
        {
            var built = await portfolio.LoadBuiltProjectAsync(projectId);
            if (built is null || built.Project.managers.Count == 0) return 0;

            var completedOn = WeeklyReminderSchedule.ToLocal(clock.GetUtcNow().UtcDateTime, config.NotifyTimezone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var url = links.Build(projectId);
            var recipients = await LoadRecipientsAsync(built.Project.managers.Select(m => m.id));
            var template = await pipeline.Templates.GetForSendAsync(EmailTemplateKeys.ProjectCompleted, ct);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(CompletionBudget);

            foreach (var managerId in built.Project.managers.Select(m => m.id))
            {
                recipients.TryGetValue(managerId.ToLowerInvariant(), out var recipient);
                var job = new Job(EmailTemplateKeys.ProjectCompleted, CompletedKey(projectId, managerId), projectId, recipient, managerId,
                    r => Task.FromResult(ProjectEmailVariables.Build(built.Project, r.FullName, url, completedOn)));
                if (await SendAsync(job, template, budget.Token) == EmailNotifyOutcome.Sent) sent++;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The completion e-mails failed for project {Project}; the status change itself is saved", projectId);
        }
        return sent;
    }

    /// <summary>Counters of one weekly pass; it gives up after {ConsecutiveFailureLimit} failed sends in a row.</summary>
    private sealed class RunTally
    {
        public int Sent { get; private set; }
        public int Duplicates { get; private set; }
        public int Skipped { get; private set; }
        public int Failed { get; private set; }
        public int FailedInARow { get; private set; }

        public bool ShouldStop(CancellationToken budget) => budget.IsCancellationRequested || FailedInARow >= ConsecutiveFailureLimit;

        public void Record(EmailNotifyOutcome outcome)
        {
            switch (outcome)
            {
                case EmailNotifyOutcome.Sent: Sent++; FailedInARow = 0; break;
                case EmailNotifyOutcome.AlreadySent: Duplicates++; break;
                case EmailNotifyOutcome.Skipped: Skipped++; break;
                default: Failed++; FailedInARow++; break;
            }
        }
    }

    private async Task RemindManagersAsync(
        PortfolioProject project, WeeklyReminderSchedule.Window window, StoredTemplate template,
        Dictionary<string, MailRecipient> recipients, RunTally tally, CancellationToken budget)
    {
        var url = links.Build(project.id);
        foreach (var managerId in project.managers.Select(m => m.id))
        {
            if (tally.ShouldStop(budget)) break;
            recipients.TryGetValue(managerId.ToLowerInvariant(), out var recipient);
            var job = new Job(EmailTemplateKeys.ProjectWeeklyReminder, WeeklyKey(project.id, managerId, window.IsoYear, window.IsoWeek), project.id, recipient, managerId,
                async r =>
                {
                    var names = project.delayedTasks > 0 ? await portfolio.LoadOverdueTaskNamesAsync(project.id, ProjectEmailVariables.OverdueListMax) : [];
                    return ProjectEmailVariables.Build(project, r.FullName, url, null, project.delayedTasks, names);
                });
            tally.Record(await SendAsync(job, template, budget));
        }
    }

    // ── ProjectWeeklyReminder ───────────────────────────────────────────────
    public async Task<WeeklyReminderSummary> RunWeeklyRemindersAsync(CancellationToken ct = default)
    {
        try
        {
            var settings = await pipeline.ReminderSettings.LoadAsync(ct);
            if (!settings.enabled) return new WeeklyReminderSummary(SkippedReason: "disabled");

            var window = WeeklyReminderSchedule.OpenWindow(clock.GetUtcNow().UtcDateTime, config.NotifyTimezone, settings.day, WeeklyReminderSettingsService.ParseTime(settings.time));
            if (window is null) return new WeeklyReminderSummary(SkippedReason: "not-due");

            var template = await pipeline.Templates.GetForSendAsync(EmailTemplateKeys.ProjectWeeklyReminder, ct);
            if (template is null || !template.IsEnabled) return new WeeklyReminderSummary(SkippedReason: template is null ? "template-missing" : "template-disabled");

            var (_, projects) = await portfolio.LoadPortfolioWithMilestonesAsync();
            // A project that is completed (or cancelled), or whose actual progress reached 100%, never gets a reminder.
            var open = projects.Where(b => b.Project.status is not ("completed" or "cancelled") && b.Project.actualProgress is not >= 100).ToList();
            var recipients = await LoadRecipientsAsync(open.SelectMany(b => b.Project.managers.Select(m => m.id)));

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(WeeklyRunBudget);
            var tally = new RunTally();
            foreach (var project in open.Select(b => b.Project))
            {
                if (tally.ShouldStop(budget.Token)) break;
                await RemindManagersAsync(project, window, template, recipients, tally, budget.Token);
            }
            if (tally.FailedInARow >= ConsecutiveFailureLimit) logger.LogWarning("Weekly project reminders stopped after {Count} failed sends in a row — the mail server looks unavailable; the rest are retried at the next check", tally.FailedInARow);
            if (tally.Sent + tally.Failed > 0) logger.LogInformation("Weekly project reminders for {Year}-W{Week}: {Sent} sent, {Failed} failed, {Duplicates} already sent, {Skipped} skipped", window.IsoYear, window.IsoWeek, tally.Sent, tally.Failed, tally.Duplicates, tally.Skipped);
            return new WeeklyReminderSummary(true, null, tally.Sent, tally.Duplicates, tally.Skipped, tally.Failed);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "The weekly project reminder job failed");
            return new WeeklyReminderSummary(SkippedReason: "error");
        }
    }
}
