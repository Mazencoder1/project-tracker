using System.Net;
using System.Text.RegularExpressions;

namespace ProjectTracker.Web.Services.Email;

/// <summary>
/// The fixed, non-editable HTML "frame" of each of the three e-mails: header, greeting, the data table, the
/// action button and the footer. None of this comes from the database and an administrator never sees it —
/// the settings page only lets them type the free-text paragraph (MessageText) that <see cref="Build"/> drops
/// into the frame. This is what keeps "لا كود HTML في الصفحة" true while the sent e-mail keeps its current look.
/// </summary>
public static partial class EmailTemplateShells
{
    private const string MessagePlaceholder = "%%MESSAGE%%";

    public const string LinkStart = "<!--link-->";
    public const string LinkEnd = "<!--/link-->";

    /// <summary>The default MessageText for a fresh install / migration backfill of each known key.</summary>
    public static string DefaultMessage(string templateKey) => templateKey switch
    {
        EmailTemplateKeys.ProjectAssigned => "تم إسناد المشروع التالي إليك، وأنت الآن مسؤول عن متابعته وتحديث بياناته:",
        EmailTemplateKeys.ProjectWeeklyReminder => "هذا تذكير أسبوعي بالمشروع التالي الذي لم ينتهِ بعد. نأمل تحديث نسب الإنجاز وتسجيل التحديث الأسبوعي:",
        EmailTemplateKeys.ProjectCompleted => "نفيدكم بأن حالة المشروع التالي تحوّلت إلى «مكتمل»، شكرًا لجهودكم:",
        _ => "",
    };

    /// <summary>Builds the full message body: the fixed frame for this key with the administrator's plain text inserted.</summary>
    public static string Build(string templateKey, string messageText) => templateKey switch
    {
        EmailTemplateKeys.ProjectAssigned => Assigned.Replace(MessagePlaceholder, MessageHtml(messageText)),
        EmailTemplateKeys.ProjectWeeklyReminder => WeeklyReminder.Replace(MessagePlaceholder, MessageHtml(messageText)),
        EmailTemplateKeys.ProjectCompleted => Completed.Replace(MessagePlaceholder, MessageHtml(messageText)),
        _ => throw new ArgumentOutOfRangeException(nameof(templateKey), templateKey, "قالب غير معروف."),
    };

    [GeneratedRegex(@"\r\n?|\n")]
    private static partial Regex NewlineRegex();

    /// <summary>Plain text → one or more &lt;p&gt; paragraphs (blank line separates paragraphs, single line break becomes &lt;br&gt;), fully HTML-encoded.</summary>
    private static string MessageHtml(string text)
    {
        var normalized = NewlineRegex().Replace((text ?? "").Trim(), "\n");
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .Select(p => $"<p>{WebUtility.HtmlEncode(p).Replace("\n", "<br>")}</p>");
        var html = string.Join("\n", paragraphs);
        return html.Length == 0 ? "" : html;
    }

    private const string Assigned =
        """
        <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid #dddddd;border-radius:8px;color:#222222;line-height:1.8">
        <h2 style="color:#134586;margin:0 0 4px">إسناد مشروع جديد</h2>
        <p style="margin:0 0 20px;color:#555555;font-size:13px">منصة متابعة المشاريع — إدارة المشاريع</p>
        <p>مرحبًا {{UserName}}،</p>
        %%MESSAGE%%
        <table role="presentation" style="width:100%;border-collapse:collapse;margin:12px 0">
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555;width:35%">اسم المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee"><strong>{{ProjectName}}</strong></td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">وصف مختصر</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectDescription}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">الإدارة</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{DepartmentName}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">مدير المشروع / المسؤول</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectManagerName}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ البداية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectStartDate}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ النهاية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectEndDate}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">نسبة الإنجاز</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectProgress}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">حالة المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectStatus}}</td></tr>
        </table>
        <!--link-->
        <p>يمكنك الدخول للمشروع من الرابط التالي:</p>
        <p style="margin:16px 0 8px"><a href="{{ProjectUrl}}" style="background:#134586;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold">فتح المشروع</a></p>
        <p style="color:#555555;font-size:13px;margin:8px 0">{{ProjectUrl}}</p>
        <!--/link-->
        <p>مع التحية.</p>
        <hr style="border:0;border-top:1px solid #dddddd;margin:24px 0 12px">
        <p style="color:#555555;font-size:12px;margin:0">رسالة آلية من منصة متابعة المشاريع، ولا حاجة للرد عليها.</p>
        </div>
        """;

    private const string WeeklyReminder =
        """
        <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid #dddddd;border-radius:8px;color:#222222;line-height:1.8">
        <h2 style="color:#134586;margin:0 0 4px">تذكير أسبوعي بمتابعة المشروع</h2>
        <p style="margin:0 0 20px;color:#555555;font-size:13px">منصة متابعة المشاريع — إدارة المشاريع</p>
        <p>مرحبًا {{UserName}}،</p>
        %%MESSAGE%%
        <table role="presentation" style="width:100%;border-collapse:collapse;margin:12px 0">
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555;width:35%">اسم المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee"><strong>{{ProjectName}}</strong></td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">الحالة الحالية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectStatus}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">نسبة الإنجاز</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectProgress}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ النهاية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectEndDate}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">الأيام المتبقية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{DaysRemaining}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">عدد المهام المتأخرة</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{OverdueTasksCount}}</td></tr>
        </table>
        {{OverdueTasksList}}
        <!--link-->
        <p style="margin:16px 0 8px"><a href="{{ProjectUrl}}" style="background:#134586;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold">فتح المشروع</a></p>
        <p style="color:#555555;font-size:13px;margin:8px 0">{{ProjectUrl}}</p>
        <!--/link-->
        <p>مع التحية.</p>
        <hr style="border:0;border-top:1px solid #dddddd;margin:24px 0 12px">
        <p style="color:#555555;font-size:12px;margin:0">رسالة آلية من منصة متابعة المشاريع، ولا حاجة للرد عليها.</p>
        </div>
        """;

    private const string Completed =
        """
        <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid #dddddd;border-radius:8px;color:#222222;line-height:1.8">
        <h2 style="color:#134586;margin:0 0 4px">اكتمل المشروع</h2>
        <p style="margin:0 0 20px;color:#555555;font-size:13px">منصة متابعة المشاريع — إدارة المشاريع</p>
        <p>مرحبًا {{UserName}}،</p>
        %%MESSAGE%%
        <table role="presentation" style="width:100%;border-collapse:collapse;margin:12px 0">
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555;width:35%">اسم المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee"><strong>{{ProjectName}}</strong></td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ الإكمال</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectCompletedDate}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">نسبة الإنجاز النهائية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectProgress}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">مدة المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectDuration}}</td></tr>
        </table>
        <!--link-->
        <p style="margin:16px 0 8px"><a href="{{ProjectUrl}}" style="background:#134586;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold">فتح المشروع</a></p>
        <p style="color:#555555;font-size:13px;margin:8px 0">{{ProjectUrl}}</p>
        <!--/link-->
        <p>مع التحية.</p>
        <hr style="border:0;border-top:1px solid #dddddd;margin:24px 0 12px">
        <p style="color:#555555;font-size:12px;margin:0">رسالة آلية من منصة متابعة المشاريع، ولا حاجة للرد عليها.</p>
        </div>
        """;
}
