using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracker.Web.Models;

// Note: جدولا ميزة إشعارات البريد الإلكتروني. على عكس بقية الكيانات (المطابقة لمخطط قائم)، أنشئ هذان الجدولان
// بترحيل EF Core مخصص (Migrations/EmailNotifications) وأسماء أعمدتهما PascalCase كما طُلب. لا مفاتيح أجنبية إلى
// profiles/projects عمدًا: حذف المستخدم/المشروع يبقى كما هو ولا يتعطل بسبب سجلات البريد.

/// <summary>A message template the administrator can edit. The key is fixed; subject, body and the switch are data.</summary>
[Table("EmailTemplates")]
public class EmailTemplate
{
    [Key] public Guid Id { get; set; }
    [MaxLength(50)] public string TemplateKey { get; set; } = "";
    [MaxLength(200)] public string NameAr { get; set; } = "";
    [MaxLength(300)] public string Subject { get; set; } = "";
    // Legacy: كانت المحرَّرة سابقًا كوسم HTML كامل من لوحة الإدارة. لم تعد مقروءة أو مكتوبة من أي كود بعد
    // الانتقال إلى MessageText (نص عادي)، وأُبقيت فقط لتفادي تغيير مخطط قاعدة بيانات مُثبَّت (راجع MessageText).
    public string BodyHtml { get; set; } = "";
    // النص الحر الذي يحرّره مدير النظام فعليًا: فقرة (أو أكثر) عادية بلا وسوم HTML. يُدرَج داخل قالب العرض
    // الثابت الخاص بكل TemplateKey (EmailTemplateShells) الذي يحمل الجدول والزر والتذييل، فلا يرى المدير أي HTML.
    public string MessageText { get; set; } = "";
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>One e-mail attempt per CorrelationKey. The unique key is what makes a duplicate send impossible.</summary>
[Table("EmailNotificationLog")]
public class EmailNotificationLog
{
    [Key] public Guid Id { get; set; }
    [MaxLength(50)] public string TemplateKey { get; set; } = "";
    public Guid? ProjectId { get; set; }
    public Guid? UserId { get; set; }
    [MaxLength(320)] public string? RecipientEmail { get; set; }
    [MaxLength(300)] public string? Subject { get; set; }
    [MaxLength(20)] public string Status { get; set; } = EmailLogStatus.Pending;
    public DateTime? SentAt { get; set; }
    [MaxLength(1000)] public string? ErrorMessage { get; set; }
    [MaxLength(300)] public string CorrelationKey { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class EmailLogStatus
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}
