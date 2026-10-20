using System.Globalization;
using System.Text.RegularExpressions;
using ProjectTracker.Web.Data;
using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Models;
using ProjectTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ProjectTracker.Web.Services.Email;

/// <summary>Weekly-reminder switch, day and time — stored in app_settings, editable by the administrator, never hard-coded.</summary>
public sealed partial class WeeklyReminderSettingsService(AppDbContext context, AppConfig config, ILogger<WeeklyReminderSettingsService> logger)
{
    public const string EnabledKey = "email_weekly_reminder_enabled";
    public const string DayKey = "email_weekly_reminder_day";
    public const string TimeKey = "email_weekly_reminder_time";

    // Used only while the three rows are missing (before the migration ran) or corrupt.
    public const int DefaultDay = 0;
    public const string DefaultTime = "08:00";

    [GeneratedRegex(@"^([01]\d|2[0-3]):([0-5]\d)$")]
    private static partial Regex TimeRegex();

    public static bool IsValidTime(string? value) => value is not null && TimeRegex().IsMatch(value);

    public static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    public async Task<WeeklyReminderSettingsVm> LoadAsync(CancellationToken ct = default)
    {
        var result = new WeeklyReminderSettingsVm { day = DefaultDay, time = DefaultTime, timeZone = config.NotifyTimezone };
        try
        {
            var stored = await context.AppSettings.AsNoTracking()
                .Where(s => s.SettingKey == EnabledKey || s.SettingKey == DayKey || s.SettingKey == TimeKey)
                .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, ct);
            if (stored.TryGetValue(EnabledKey, out var enabled)) result.enabled = !string.Equals(enabled.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            if (stored.TryGetValue(DayKey, out var day) && int.TryParse(day.Trim(), out var d) && d is >= 0 and <= 6) result.day = d;
            if (stored.TryGetValue(TimeKey, out var time) && IsValidTime(time.Trim())) result.time = time.Trim();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the weekly reminder settings; the defaults are used");
        }
        return result;
    }

    /// <summary>Errors per field (Arabic) or empty when saved.</summary>
    public async Task<Dictionary<string, string>> SaveAsync(bool? enabled, int? day, string? time, string? userId, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (day is < 0 or > 6) errors["day"] = "اليوم يجب أن يكون بين 0 (الأحد) و6 (السبت).";
        if (time is not null && !IsValidTime(time.Trim())) errors["time"] = "الوقت يجب أن يكون بصيغة HH:mm (24 ساعة)، مثل 08:00.";
        if (errors.Count > 0) return errors;

        var updatedBy = Js.IsUuid(userId) ? Guid.Parse(userId!) : (Guid?)null;
        var values = new Dictionary<string, string>();
        if (enabled is { } e) values[EnabledKey] = e ? "true" : "false";
        if (day is { } d) values[DayKey] = d.ToString(CultureInfo.InvariantCulture);
        if (time is not null) values[TimeKey] = time.Trim();

        var existing = await context.AppSettings.Where(s => values.Keys.Contains(s.SettingKey)).ToDictionaryAsync(s => s.SettingKey, ct);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var row))
            {
                row.SettingValue = value;
                row.UpdatedBy = updatedBy;
                row.UpdatedAt = DateTime.Now;
            }
            else
            {
                context.AppSettings.Add(new AppSettingRow { SettingKey = key, SettingValue = value, UpdatedBy = updatedBy });
            }
        }
        await context.SaveChangesAsync(ct);
        return errors;
    }
}

/// <summary>The weekly window: which ISO week a reminder belongs to, and whether "now" is inside it. Pure.</summary>
public static class WeeklyReminderSchedule
{
    /// <summary>A missed reminder is still sent for up to this long after its time (the server was down, a cycle failed); after that the week is skipped.</summary>
    public static readonly TimeSpan CatchUp = TimeSpan.FromHours(24);

    public sealed record Window(int IsoYear, int IsoWeek, DateTime ScheduledLocal);

    public static DateTime ToLocal(DateTime utcNow, string timeZone)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch (TimeZoneNotFoundException)
        {
            zone = timeZone == NotifyRules.DefaultTimezone ? TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time") : TimeZoneInfo.Utc;
        }
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
    }

    /// <summary>
    /// The window that is open at <paramref name="utcNow"/>, or null. The scheduled instant is the latest occurrence of
    /// (day, time) that is not in the future; the window stays open for <see cref="CatchUp"/> after it. The ISO
    /// year/week of that instant is the "period" the once-per-week send key is built from.
    /// </summary>
    public static Window? OpenWindow(DateTime utcNow, string timeZone, int day, TimeOnly time)
    {
        var local = ToLocal(utcNow, timeZone);
        var daysBack = ((int)local.DayOfWeek - day + 7) % 7;
        var scheduled = local.Date.AddDays(-daysBack) + time.ToTimeSpan();
        if (scheduled > local) scheduled = scheduled.AddDays(-7);
        if (local - scheduled >= CatchUp) return null;
        return new Window(System.Globalization.ISOWeek.GetYear(scheduled), System.Globalization.ISOWeek.GetWeekOfYear(scheduled), scheduled);
    }
}
