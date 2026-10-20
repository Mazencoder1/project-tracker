using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectTracker.Web.Migrations.EmailNotifications
{
    /// <inheritdoc />
    public partial class AddEmailTemplateMessageText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MessageText",
                table: "EmailTemplates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // Note: من الآن، محرِّر الإعدادات يحرّر فقرة نص عادية فقط (MessageText) بدل وسم HTML كامل (BodyHtml).
            // BodyHtml أُبقي في الجدول دون حذف تفاديًا لأي فقدان بيانات، لكنه لم يعد يُقرأ من أي كود بعد هذا
            // الترحيل: الإطار الثابت لكل رسالة (الترويسة والجدول والزر) بات في EmailTemplateShells (كود لا قاعدة
            // بيانات)، وهذا الترحيل يعبّئ MessageText بنفس جملة الافتتاح الافتراضية لكل قالب من القوالب الثلاثة
            // المعروفة. أي تخصيص سابق كان قد أُدخل يدويًا في BodyHtml الخام لا يُنقل تلقائيًا (تعذّر استخراجه
            // بأمان من HTML حر)؛ يعيد مدير النظام كتابته كنص عادي من الصفحة نفسها إذا احتاج.
            migrationBuilder.Sql(
                """
                UPDATE [EmailTemplates] SET [MessageText] = N'تم إسناد المشروع التالي إليك، وأنت الآن مسؤول عن متابعته وتحديث بياناته:'
                WHERE [TemplateKey] = N'ProjectAssigned';

                UPDATE [EmailTemplates] SET [MessageText] = N'هذا تذكير أسبوعي بالمشروع التالي الذي لم ينتهِ بعد. نأمل تحديث نسب الإنجاز وتسجيل التحديث الأسبوعي:'
                WHERE [TemplateKey] = N'ProjectWeeklyReminder';

                UPDATE [EmailTemplates] SET [MessageText] = N'نفيدكم بأن حالة المشروع التالي تحوّلت إلى «مكتمل»، شكرًا لجهودكم:'
                WHERE [TemplateKey] = N'ProjectCompleted';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessageText",
                table: "EmailTemplates");
        }
    }
}
