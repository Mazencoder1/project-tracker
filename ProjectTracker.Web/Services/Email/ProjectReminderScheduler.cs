namespace ProjectTracker.Web.Services.Email;

/// <summary>
/// The weekly project-reminder job (a BackgroundService, no external cron). It wakes up every few minutes
/// (Notifications:ReminderCheckMinutes), asks the notifier whether the configured weekly window is open and, if so,
/// sends the reminders that have not been sent for this ISO week yet. It is safe to restart at any time and to run on
/// several servers at once: what was sent is remembered in EmailNotificationLog, not in this process.
/// It is switched on exactly like the existing digest scheduler (Notifications:SchedulerEnabled / NOTIFY_SCHEDULER).
/// </summary>
public sealed class ProjectReminderScheduler(IServiceScopeFactory scopes, AppConfig config, ILogger<ProjectReminderScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.NotifyScheduler) return;
        if (!config.IsProduction && !config.NotifySchedulerInDev) return;

        var interval = TimeSpan.FromMinutes(config.ReminderCheckMinutes);
        logger.LogInformation("The weekly project reminder job is on — it checks every {Minutes} minutes.", config.ReminderCheckMinutes);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
            using var timer = new PeriodicTimer(interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IProjectEmailNotifier>().RunWeeklyRemindersAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "A weekly project reminder check failed");
        }
    }
}
