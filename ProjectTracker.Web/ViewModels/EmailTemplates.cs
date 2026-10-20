#pragma warning disable IDE1006 // Property names are the JSON field names the settings page reads.

namespace ProjectTracker.Web.ViewModels;

public sealed class EmailVariableVm
{
    public string name { get; set; } = "";
    public string label { get; set; } = "";
    public string sample { get; set; } = "";
    public bool isHtml { get; set; }
}

public sealed class EmailTemplateVm
{
    public string templateKey { get; set; } = "";
    public string nameAr { get; set; } = "";
    public string subject { get; set; } = "";
    /// <summary>Plain text only — the fixed HTML frame (header/table/button/footer) is never sent to or edited from the client.</summary>
    public string messageText { get; set; } = "";
    public bool isEnabled { get; set; }
    public string whenSent { get; set; } = "";
    public string updatedAt { get; set; } = "";
    public string? updatedByName { get; set; }
}

public sealed class EmailPreviewVm
{
    public string subject { get; set; } = "";
    /// <summary>The final rendered HTML e-mail (frame + message + real values), for the preview iframe only.</summary>
    public string bodyHtml { get; set; } = "";
}

public sealed class WeeklyReminderSettingsVm
{
    public bool enabled { get; set; } = true;
    /// <summary>0 = Sunday … 6 = Saturday.</summary>
    public int day { get; set; }
    /// <summary>HH:mm in the system time zone.</summary>
    public string time { get; set; } = "08:00";
    public string timeZone { get; set; } = "";
}
