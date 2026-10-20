using System.Globalization;
using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.ViewModels;
using static ProjectTracker.Web.Services.NotifyTemplates;

namespace ProjectTracker.Web.Services;

public sealed record Recipient(string Id, string FullName, string Email, string Role);
public sealed record ZonedNow(int Weekday, int Hour, string Date);
public sealed record DelayAlert(Recipient Recipient, PortfolioProject Project, List<MilestoneSummary> Milestones);
public sealed record ProjectDelayAlert(Recipient Recipient, PortfolioProject Project);
public sealed record MailContent(string Subject, string Html);

public enum SendOnceOutcome { Sent, AlreadySent, Failed }

/// <summary>Timing and selection rules for the automatic e-mails (port of lib/notifyRules.ts). Pure functions.</summary>
public static class NotifyRules
{
    public static readonly string[] NeedsAction = ["stopped", "at_risk", "delayed", "needs_attention"];
    public static readonly string[] PortfolioRoles = ["admin", "deputy"];
    public const string DefaultTimezone = "Asia/Riyadh";
    public const int DefaultWeeklyHour = 7;

    /// <summary>Weekday (0 = Sunday), hour and calendar date in a time zone — "Sunday" is Sunday in Riyadh, not on the server.</summary>
    public static ZonedNow ZonedNowIn(DateTime utcNow, string timeZone)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch (TimeZoneNotFoundException)
        {
            zone = timeZone == DefaultTimezone ? TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time") : TimeZoneInfo.Utc;
        }
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        return new ZonedNow((int)local.DayOfWeek, local.Hour, local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>The Sunday that starts the current week (yyyy-MM-dd) — the "already sent this week" reference.</summary>
    public static string WeekRef(ZonedNow now)
    {
        var d = DateOnly.ParseExact(now.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-now.Weekday);
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static bool IsWeeklyDue(ZonedNow now, int weeklyHour = DefaultWeeklyHour) => now.Weekday == 0 && now.Hour >= weeklyHour;

    public static List<MilestoneSummary> DelayedMilestones(IEnumerable<MilestoneSummary> milestones) => milestones.Where(m => m.state == "delayed").ToList();

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static List<BuiltProject> ProjectsOf(IEnumerable<BuiltProject> all, string userId) =>
        all.Where(p => p.Project.managers.Any(m => SameId(m.id, userId))).ToList();

    public static List<ProjectDelayAlert> CollectProjectDelayAlerts(IEnumerable<BuiltProject> projects, IEnumerable<Recipient> recipients)
    {
        var byId = recipients.ToDictionary(r => r.Id.ToLowerInvariant());
        var alerts = new List<ProjectDelayAlert>();
        foreach (var (project, _, _) in projects)
        {
            if (!IsLate(project)) continue;
            foreach (var manager in project.managers)
                if (byId.TryGetValue(manager.id.ToLowerInvariant(), out var recipient)) alerts.Add(new(recipient, project));
        }
        return alerts;
    }

    /// <summary>
    /// «متأخر» = الصحة delayed **أو** تجاوز تاريخ النهاية. كان الشرط الصحة وحدها، فمشروع متجاوز للمدة
    /// صحته at_risk/stopped (أشد) لا يصل مسؤوله أي تنبيه. المكتمل/الملغى/البالغ 100% لا يُنبَّه أبدًا.
    /// </summary>
    public static bool IsLate(PortfolioProject project) =>
        project.status is not ("completed" or "cancelled") && project.actualProgress is not >= 100
        && (project.health == "delayed" || project.isOverdue);

    /// <summary>Projects that are no longer late (or are completed) — their delay-alert reservation is released.</summary>
    public static List<string> RecoveredProjectIds(IEnumerable<BuiltProject> projects) =>
        projects.Where(p => !IsLate(p.Project)).Select(p => p.Project.id.ToLowerInvariant()).ToList();

    public static List<DelayAlert> CollectDelayAlerts(IEnumerable<BuiltProject> projects, IEnumerable<Recipient> recipients)
    {
        var byId = recipients.ToDictionary(r => r.Id.ToLowerInvariant());
        var alerts = new List<DelayAlert>();
        foreach (var (project, milestones, _) in projects)
        {
            if (project.status == "completed") continue;
            var late = DelayedMilestones(milestones);
            if (late.Count == 0) continue;
            foreach (var manager in project.managers)
                if (byId.TryGetValue(manager.id.ToLowerInvariant(), out var recipient)) alerts.Add(new(recipient, project, late));
        }
        return alerts;
    }
}

public sealed class RunSummary
{
    public WeeklyCounter weekly { get; set; } = new();
    public Counter delays { get; set; } = new();
    public ProjectDelayCounter projectDelays { get; set; } = new();

    public class Counter { public int sent { get; set; } public int skipped { get; set; } public int failed { get; set; } }
    public sealed class WeeklyCounter : Counter { public bool ran { get; set; } }
    public sealed class ProjectDelayCounter : Counter { public int recovered { get; set; } }
}

/// <summary>
/// The automatic e-mails: assignment, weekly digest, milestone delay, project delay (ports of lib/notifyRun.ts,
/// lib/notifyDigest.ts and lib/notifyLog.ts). notification_log's unique key (user, kind, ref) makes duplicates
/// impossible: a message is reserved before it is sent and released if sending fails.
/// </summary>
public sealed class NotificationService(SqlDb db, PortfolioQueries portfolio, IMailService mail, NotifyTemplates templates, AppConfig config, ILogger<NotificationService> logger)
{
    private static readonly Dictionary<string, string> HealthLabel = new()
    {
        ["on_track"] = "ضمن الخطة", ["needs_attention"] = "يحتاج متابعة", ["delayed"] = "متأخر", ["at_risk"] = "متعثر",
        ["stopped"] = "متوقف", ["completed"] = "مكتمل", ["unknown"] = "غير متوفر",
    };

    private static readonly Dictionary<string, string> ProjectStatusLabel = new()
    {
        ["active"] = "نشط", ["completed"] = "مكتمل", ["delayed"] = "متأخر", ["cancelled"] = "ملغى",
    };

    private const string RecipientWhere = "ISNULL(status, 'active') = 'active' AND email IS NOT NULL AND LTRIM(RTRIM(email)) <> ''";

    public ZonedNow CurrentZonedNow() => NotifyRules.ZonedNowIn(DateTime.UtcNow, config.NotifyTimezone);

    // ── recipients ──────────────────────────────────────────────────────────
    private static Recipient ToRecipient(Row r) => new(r.Str("id")!, r.Str("full_name") ?? "", (r.Str("email") ?? "").Trim(), r.Str("role") ?? "");

    private async Task<List<Recipient>> LoadRecipientsAsync() =>
        (await db.QueryAsync($"SELECT id, full_name, email, role FROM profiles WHERE {RecipientWhere}")).Select(ToRecipient).ToList();

    // ── e-mail bodies ───────────────────────────────────────────────────────
    private static string Pct(int? value) => value is null ? "غير متوفر" : $"{value}%";

    private static string VarianceText(int? variance) => variance switch
    {
        null => "غير متوفر",
        > 0 => $"+{variance}% (متقدّم)",
        < 0 => $"{variance}% (متأخر)",
        _ => "0% (مطابق للخطة)",
    };

    private static readonly string[] ProjectHeaders = ["المشروع", "الصحة", "فعلي / مخطط", "الانحراف", "المرحلة الحالية", "مراحل متأخرة", "مخاطر مفتوحة"];

    private static List<string[]> ProjectRows(IEnumerable<BuiltProject> projects) => projects.Select(b =>
    {
        var late = NotifyRules.DelayedMilestones(b.Milestones).Count;
        return new[]
        {
            Escape(b.Project.name),
            Escape(HealthLabel[b.Project.health]),
            $"{Escape(Pct(b.Project.actualProgress))} / {Escape(Pct(b.Project.plannedProgress))}",
            Escape(VarianceText(b.Project.variance)),
            Escape(b.Project.currentMilestone?.name ?? (b.Milestones.Count == 0 ? "لا توجد مراحل" : "غير محددة")),
            late > 0 ? Bold(late.ToString()) : "0",
            b.Project.openRisks is null ? "غير مُدخلة" : b.Project.openRisks.ToString()!,
        };
    }).ToList();

    private static string PmDigestBody(Recipient recipient, List<BuiltProject> mine)
    {
        var stale = mine.Where(p => p.Project.isStale).ToList();
        var late = mine.Where(p => NotifyRules.DelayedMilestones(p.Milestones).Count > 0).ToList();
        var attention = mine.Where(p => NotifyRules.NeedsAction.Contains(p.Project.health)).ToList();
        var noRisks = mine.Where(p => p.Project.openRisks is null).ToList();

        var todo = new List<string>();
        if (stale.Count > 0) todo.Add($"سجّل التحديث الأسبوعي لـ: {string.Join("، ", stale.Select(p => p.Project.name))}");
        if (late.Count > 0) todo.Add($"راجع المراحل المتأخرة في: {string.Join("، ", late.Select(p => p.Project.name))}");
        if (noRisks.Count > 0) todo.Add($"لم تُدخل بيانات المخاطر في: {string.Join("، ", noRisks.Select(p => p.Project.name))}");

        return string.Concat(
            Paragraph($"مرحبًا {recipient.FullName}، هذا ملخص مشاريعك لبداية الأسبوع."),
            attention.Count > 0
                ? Callout($"{attention.Count} من {mine.Count} من مشاريعك تحتاج تدخلًا هذا الأسبوع.", "danger")
                : Callout("كل مشاريعك ضمن الخطة حسب البيانات المسجّلة.", "ok"),
            Table(ProjectHeaders, ProjectRows(mine)),
            todo.Count > 0 ? Paragraph("المطلوب منك هذا الأسبوع:") + BulletList(todo) : "");
    }

    private static string PortfolioDigestBody(Recipient recipient, List<BuiltProject> all)
    {
        var attention = all.Where(p => NotifyRules.NeedsAction.Contains(p.Project.health))
            .OrderBy(p => p.Project.attentionRank ?? 99).ToList();
        var stale = all.Where(p => p.Project.isStale).ToList();
        var overdue = all.Where(p => p.Project.isOverdue).ToList();
        var totalLate = all.Sum(p => NotifyRules.DelayedMilestones(p.Milestones).Count);

        return string.Concat(
            Paragraph($"مرحبًا {recipient.FullName}، هذا ملخص المحفظة لبداية الأسبوع."),
            BulletList([
                $"إجمالي المشاريع: {all.Count}",
                $"تحتاج تدخلًا: {attention.Count}",
                $"بلا تحديث أسبوعي حديث: {stale.Count}",
                $"تجاوزت تاريخ نهايتها: {overdue.Count}",
                $"مراحل متأخرة في المحفظة: {totalLate}",
            ]),
            attention.Count > 0
                ? Paragraph("المشاريع التي تتصدّر الأولوية:") + Table(ProjectHeaders, ProjectRows(attention.Take(10)))
                : Callout("لا توجد مشاريع تحتاج تدخلًا حسب البيانات الحالية.", "ok"),
            stale.Count > 0
                ? Callout($"لم يُسجَّل تحديث أسبوعي حديث في {ArabicCount.Format(stale.Count, "project")} — التحديث مسؤولية مدير المشروع المُسند.")
                : "");
    }

    private MailContent? WeeklyEmail(Recipient recipient, List<BuiltProject> all)
    {
        if (NotifyRules.PortfolioRoles.Contains(recipient.Role))
        {
            if (all.Count == 0) return null;
            return new("الملخص الأسبوعي لمحفظة المشاريع",
                templates.Layout("ملخص بداية الأسبوع", PortfolioDigestBody(recipient, all), "/meeting", "فتح لوحة متابعة المشاريع"));
        }

        var mine = NotifyRules.ProjectsOf(all, recipient.Id);
        if (mine.Count == 0) return null;
        return new($"الملخص الأسبوعي لمشاريعك ({mine.Count})",
            templates.Layout("ملخص بداية الأسبوع", PmDigestBody(recipient, mine), "/pm", "فتح صفحة مشاريعي"));
    }

    private MailContent ProjectDelayEmail(ProjectDelayAlert alert)
    {
        var p = alert.Project;
        var facts = new List<string>();
        if (!string.IsNullOrEmpty(p.project_code)) facts.Add($"رقم المشروع: {p.project_code}");
        if (!string.IsNullOrEmpty(p.end_date))
            facts.Add(p.daysToEnd is < 0
                ? $"تاريخ النهاية: {p.end_date} — تجاوزه بـ {ArabicCount.Format(Math.Abs(p.daysToEnd.Value), "day")}"
                : $"تاريخ النهاية: {p.end_date}");
        facts.Add($"الإنجاز الفعلي مقابل المخطط: {Pct(p.actualProgress)} / {Pct(p.plannedProgress)}");
        facts.Add($"الانحراف: {VarianceText(p.variance)}");

        var body = string.Concat(
            Paragraph($"مرحبًا {alert.Recipient.FullName}،"),
            Callout($"مشروع {p.name} حالته الآن: متأخر.", "danger"),
            p.healthReasons.Count > 0 ? Paragraph("سبب التصنيف:") + BulletList(p.healthReasons) : "",
            Paragraph("بيانات المشروع:"),
            BulletList(facts),
            Paragraph("المطلوب:"),
            BulletList([
                "سجّل التحديث الأسبوعي موضّحًا سبب التأخير والخطة التصحيحية.",
                "حدّث نسب إنجاز المهام والمراحل إن كان التقدّم غير مسجّل.",
                "اطلب الدعم إن كان سبب التأخير خارج سيطرتك.",
            ]));
        return new($"تنبيه: تأخّر مشروع {p.name}", templates.Layout("تنبيه تأخّر مشروع", body, "/pm", "فتح صفحة مشاريعي"));
    }

    private MailContent DelayEmail(DelayAlert alert)
    {
        var rows = alert.Milestones.Select(m => new[]
        {
            Escape(m.name),
            Bold(ArabicCount.Format(m.delayWeeks, "week")),
            $"{Escape(Pct(m.progress))} / {Escape(Pct(m.plannedProgress))}",
            Escape($"أسبوع {m.week_start} — {m.week_end}"),
        });
        var body = string.Concat(
            Paragraph($"مرحبًا {alert.Recipient.FullName}،"),
            Callout(alert.Milestones.Count == 1
                ? $"تأخّرت مرحلة في مشروع {alert.Project.name}."
                : $"تأخّرت {ArabicCount.Format(alert.Milestones.Count, "milestone")} في مشروع {alert.Project.name}.", "danger"),
            Table(["المرحلة", "مدة التأخّر", "فعلي / مخطط", "النطاق المخطط"], rows),
            Paragraph($"حالة المشروع: {ProjectStatusLabel.GetValueOrDefault(alert.Project.status, alert.Project.status)} — الصحة: {HealthLabel[alert.Project.health]}."),
            Paragraph("المطلوب:"),
            BulletList([
                "حدّث نسب إنجاز مهام المرحلة إن كان التقدّم غير مسجّل.",
                "سجّل سبب التأخير في التحديث الأسبوعي.",
                "اطلب الدعم إن كان التأخير خارج سيطرتك.",
            ]));
        return new($"تنبيه: تأخّر مرحلة في مشروع {alert.Project.name}", templates.Layout("تنبيه تأخّر مرحلة", body, "/pm", "فتح صفحة مشاريعي"));
    }

    // ── send-once log ───────────────────────────────────────────────────────
    private async Task<SendOnceOutcome> SendOnceAsync(string userId, string kind, string reference, string? detail, Func<Task<NotificationResult>> send)
    {
        try
        {
            await db.ExecuteAsync("INSERT INTO notification_log (user_id, kind, ref, detail) VALUES (@user_id, @kind, @ref, @detail)",
                [SqlDb.Guid("@user_id", userId), SqlDb.Text("@kind", kind, 40), SqlDb.Text("@ref", reference, 200), SqlDb.Text("@detail", detail, 400)]);
        }
        catch (Exception ex) when (SqlDb.SqlErrorNumber(ex) is 2627 or 2601)
        {
            return SendOnceOutcome.AlreadySent;
        }

        NotificationResult result;
        try
        {
            result = await send();
        }
        catch (Exception ex)
        {
            await ReleaseClaimAsync(userId, kind, reference);
            logger.LogError(ex, "Notification {Kind} threw while sending", kind);
            return SendOnceOutcome.Failed;
        }

        if (!result.Ok)
        {
            await ReleaseClaimAsync(userId, kind, reference);
            logger.LogError("Notification {Kind} was not sent ({Reason}): {Message}", kind, result.Reason, result.Message);
            return SendOnceOutcome.Failed;
        }
        return SendOnceOutcome.Sent;
    }

    private async Task ReleaseClaimAsync(string userId, string kind, string reference)
    {
        try
        {
            await db.ExecuteAsync("DELETE FROM notification_log WHERE user_id = @user_id AND kind = @kind AND ref = @ref",
                [SqlDb.Guid("@user_id", userId), SqlDb.Text("@kind", kind, 40), SqlDb.Text("@ref", reference, 200)]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not release a notification reservation");
        }
    }

    private Task<NotificationResult> Deliver(Recipient recipient, MailContent content) =>
        mail.SendAsync(new MailRequest([recipient.Email], content.Subject, content.Html));

    // ── the four notifications ──────────────────────────────────────────────
    // Note: رسالة «أُسند إليك مشروع» لم تعد هنا: انتقلت إلى نظام القوالب (Services/Email، القالب ProjectAssigned في القاعدة).

    private static void Tally(RunSummary.Counter counter, SendOnceOutcome outcome)
    {
        if (outcome == SendOnceOutcome.Sent) counter.sent++;
        else if (outcome == SendOnceOutcome.AlreadySent) counter.skipped++;
        else counter.failed++;
    }

    public async Task<RunSummary.WeeklyCounter> RunWeeklyDigestAsync(bool force = false)
    {
        var counter = new RunSummary.WeeklyCounter();
        var now = CurrentZonedNow();
        if (!force && !NotifyRules.IsWeeklyDue(now, config.NotifyWeeklyHour)) return counter;
        counter.ran = true;

        var reference = NotifyRules.WeekRef(now);
        var (_, projects) = await portfolio.LoadPortfolioWithMilestonesAsync();
        foreach (var recipient in await LoadRecipientsAsync())
        {
            var content = WeeklyEmail(recipient, projects);
            if (content is null) continue;
            Tally(counter, await SendOnceAsync(recipient.Id, "weekly", reference, content.Subject, () => Deliver(recipient, content)));
        }
        return counter;
    }

    private async Task<List<MilestoneSummary>> ClaimMilestonesAsync(DelayAlert alert, RunSummary.Counter counter)
    {
        var fresh = new List<MilestoneSummary>();
        foreach (var milestone in alert.Milestones)
        {
            var claimed = await SendOnceAsync(alert.Recipient.Id, "milestone_delay", milestone.id.ToLowerInvariant(), $"تأخّر {milestone.name}",
                () => Task.FromResult(NotificationResult.Success));
            if (claimed == SendOnceOutcome.Sent) fresh.Add(milestone);
            else if (claimed == SendOnceOutcome.AlreadySent) counter.skipped++;
            else counter.failed++;
        }
        return fresh;
    }

    public async Task<RunSummary.Counter> RunDelayAlertsAsync()
    {
        var counter = new RunSummary.Counter();
        var (_, projects) = await portfolio.LoadPortfolioWithMilestonesAsync();
        foreach (var alert in NotifyRules.CollectDelayAlerts(projects, await LoadRecipientsAsync()))
        {
            // Reserve each late milestone first, then send one e-mail for the newly reserved ones.
            var fresh = await ClaimMilestonesAsync(alert, counter);
            if (fresh.Count == 0) continue;

            var content = DelayEmail(alert with { Milestones = fresh });
            var result = await Deliver(alert.Recipient, content);
            if (result.Ok)
            {
                counter.sent += fresh.Count;
            }
            else
            {
                counter.failed += fresh.Count;
                logger.LogError("Milestone delay alert was not sent ({Reason}): {Message}", result.Reason, result.Message);
                foreach (var m in fresh) await ReleaseClaimAsync(alert.Recipient.Id, "milestone_delay", m.id.ToLowerInvariant());
            }
        }
        return counter;
    }

    public async Task<RunSummary.ProjectDelayCounter> RunProjectDelayAlertsAsync()
    {
        var counter = new RunSummary.ProjectDelayCounter();
        var (_, projects) = await portfolio.LoadPortfolioWithMilestonesAsync();

        counter.recovered = await ReleaseProjectDelayClaimsAsync(NotifyRules.RecoveredProjectIds(projects));

        foreach (var alert in NotifyRules.CollectProjectDelayAlerts(projects, await LoadRecipientsAsync()))
        {
            var content = ProjectDelayEmail(alert);
            Tally(counter, await SendOnceAsync(alert.Recipient.Id, "project_delay", alert.Project.id.ToLowerInvariant(), content.Subject,
                () => Deliver(alert.Recipient, content)));
        }
        return counter;
    }

    /// <summary>A recovered project frees its reservation, so a later relapse alerts again.</summary>
    private async Task<int> ReleaseProjectDelayClaimsAsync(List<string> projectIds)
    {
        if (projectIds.Count == 0) return 0;
        const int chunkSize = 200;
        var removed = 0;
        try
        {
            for (var i = 0; i < projectIds.Count; i += chunkSize)
            {
                var chunk = projectIds.Skip(i).Take(chunkSize).ToList();
                var parameters = chunk.Select((id, j) => SqlDb.Text($"@ref{j}", id, 200)).ToList();
                removed += await db.ExecuteAsync(
                    $"DELETE FROM notification_log WHERE kind = 'project_delay' AND ref IN ({string.Join(", ", parameters.Select(p => p.ParameterName))})", parameters);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not release the project delay reservations");
        }
        return removed;
    }

    /// <summary>One full pass: project delays, milestone delays, then the weekly digest (Sunday only unless forced).</summary>
    public async Task<RunSummary> RunCycleAsync(bool forceWeekly = false)
    {
        var projectDelays = await RunProjectDelayAlertsAsync();
        var delays = await RunDelayAlertsAsync();
        var weekly = await RunWeeklyDigestAsync(forceWeekly);
        if (weekly.ran)
        {
            try
            {
                await db.ExecuteAsync("DELETE FROM notification_log WHERE kind <> 'assignment' AND sent_at < DATEADD(year, -1, SYSUTCDATETIME())");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not prune the notification log");
            }
        }
        return new RunSummary { weekly = weekly, delays = delays, projectDelays = projectDelays };
    }
}

/// <summary>Built-in scheduler (NOTIFY_SCHEDULER=true): one cycle every NOTIFY_SCHEDULER_MINUTES, safe next to an external trigger.</summary>
public sealed class NotificationScheduler(IServiceScopeFactory scopes, AppConfig config, ILogger<NotificationScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.NotifyScheduler) return;
        if (!config.IsProduction && !config.NotifySchedulerInDev) return;

        var interval = TimeSpan.FromMinutes(config.NotifySchedulerMinutes);
        logger.LogInformation("The notification scheduler is on — one cycle every {Minutes} minutes.", config.NotifySchedulerMinutes);
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                await CycleAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task CycleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var summary = await scope.ServiceProvider.GetRequiredService<NotificationService>().RunCycleAsync();
            var sent = summary.weekly.sent + summary.delays.sent + summary.projectDelays.sent;
            var failed = summary.weekly.failed + summary.delays.failed + summary.projectDelays.failed;
            if (sent > 0 || failed > 0) logger.LogInformation("Notification cycle: {@Summary}", summary);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "The notification cycle failed");
        }
    }
}
