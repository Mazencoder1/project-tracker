namespace ProjectTracker.Web.Migrations.EmailNotifications;

/// <summary>
/// First-install content of the three templates, written to the EmailTemplates table by the migration. After that
/// the database row is the only source: the administrator edits it from the settings page and no application code
/// contains message text. (Changing this file does not change an installed system.)
/// </summary>
public static class EmailTemplateSeeds
{
    public sealed record Seed(string Key, string NameAr, string Subject, string BodyHtml);

    public static readonly IReadOnlyList<Seed> All = [Assigned, WeeklyReminder, Completed];

    private static Seed Assigned => new("ProjectAssigned", "إسناد مشروع", "تم إسناد مشروع جديد لك - {{ProjectName}}",
        """
        <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid #dddddd;border-radius:8px;color:#222222;line-height:1.8">
        <h2 style="color:#134586;margin:0 0 4px">إسناد مشروع جديد</h2>
        <p style="margin:0 0 20px;color:#555555;font-size:13px">نظام متابعة المشاريع — إدارة المشاريع</p>
        <p>مرحبًا {{UserName}}،</p>
        <p>تم إسناد المشروع التالي إليك، وأنت الآن مسؤول عن متابعته وتحديث بياناته:</p>
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
        <p>يمكنك الدخول للمشروع من الرابط التالي:</p>
        <p style="margin:16px 0 8px"><a href="{{ProjectUrl}}" style="background:#134586;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold">فتح المشروع</a></p>
        <p style="color:#555555;font-size:13px;margin:8px 0">{{ProjectUrl}}</p>
        <p>مع التحية.</p>
        <hr style="border:0;border-top:1px solid #dddddd;margin:24px 0 12px">
        <p style="color:#555555;font-size:12px;margin:0">رسالة آلية من نظام متابعة المشاريع، ولا حاجة للرد عليها.</p>
        </div>
        """);

    private static Seed WeeklyReminder => new("ProjectWeeklyReminder", "التذكير الأسبوعي بالمشروع", "تذكير أسبوعي بمتابعة المشروع - {{ProjectName}}",
        """
        <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid #dddddd;border-radius:8px;color:#222222;line-height:1.8">
        <h2 style="color:#134586;margin:0 0 4px">تذكير أسبوعي بمتابعة المشروع</h2>
        <p style="margin:0 0 20px;color:#555555;font-size:13px">نظام متابعة المشاريع — إدارة المشاريع</p>
        <p>مرحبًا {{UserName}}،</p>
        <p>هذا تذكير أسبوعي بالمشروع التالي الذي لم ينتهِ بعد. نأمل تحديث نسب الإنجاز وتسجيل التحديث الأسبوعي:</p>
        <table role="presentation" style="width:100%;border-collapse:collapse;margin:12px 0">
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555;width:35%">اسم المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee"><strong>{{ProjectName}}</strong></td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">الحالة الحالية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectStatus}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">نسبة الإنجاز</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectProgress}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ النهاية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectEndDate}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">الأيام المتبقية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{DaysRemaining}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">عدد المهام المتأخرة</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{OverdueTasksCount}}</td></tr>
        </table>
        {{OverdueTasksList}}
        <p style="margin:16px 0 8px"><a href="{{ProjectUrl}}" style="background:#134586;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold">فتح المشروع</a></p>
        <p style="color:#555555;font-size:13px;margin:8px 0">{{ProjectUrl}}</p>
        <p>مع التحية.</p>
        <hr style="border:0;border-top:1px solid #dddddd;margin:24px 0 12px">
        <p style="color:#555555;font-size:12px;margin:0">رسالة آلية من نظام متابعة المشاريع، ولا حاجة للرد عليها.</p>
        </div>
        """);

    private static Seed Completed => new("ProjectCompleted", "اكتمال المشروع", "تم إكمال المشروع - {{ProjectName}}",
        """
        <div dir="rtl" style="font-family:Tahoma,Arial,sans-serif;max-width:640px;margin:auto;padding:24px;border:1px solid #dddddd;border-radius:8px;color:#222222;line-height:1.8">
        <h2 style="color:#134586;margin:0 0 4px">اكتمل المشروع</h2>
        <p style="margin:0 0 20px;color:#555555;font-size:13px">نظام متابعة المشاريع — إدارة المشاريع</p>
        <p>مرحبًا {{UserName}}،</p>
        <p>نفيدكم بأن حالة المشروع التالي تحوّلت إلى «مكتمل»، شكرًا لجهودكم:</p>
        <table role="presentation" style="width:100%;border-collapse:collapse;margin:12px 0">
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555;width:35%">اسم المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee"><strong>{{ProjectName}}</strong></td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">تاريخ الإكمال</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectCompletedDate}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">نسبة الإنجاز النهائية</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectProgress}}</td></tr>
        <tr><td style="padding:8px;border-bottom:1px solid #eeeeee;color:#555555">مدة المشروع</td><td style="padding:8px;border-bottom:1px solid #eeeeee">{{ProjectDuration}}</td></tr>
        </table>
        <p style="margin:16px 0 8px"><a href="{{ProjectUrl}}" style="background:#134586;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:6px;display:inline-block;font-weight:bold">فتح المشروع</a></p>
        <p style="color:#555555;font-size:13px;margin:8px 0">{{ProjectUrl}}</p>
        <p>مع التحية.</p>
        <hr style="border:0;border-top:1px solid #dddddd;margin:24px 0 12px">
        <p style="color:#555555;font-size:12px;margin:0">رسالة آلية من نظام متابعة المشاريع، ولا حاجة للرد عليها.</p>
        </div>
        """);
}
