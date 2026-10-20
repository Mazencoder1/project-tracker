using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectTracker.Web.Migrations.EmailNotifications
{
    /// <inheritdoc />
    public partial class EmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailNotificationLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    TemplateKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrelationKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailNotificationLog", x => x.Id);
                    table.CheckConstraint("CK_EmailNotificationLog_Status", "[Status] IN (N'Pending', N'Sent', N'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    TemplateKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailNotificationLog_Project_Template",
                table: "EmailNotificationLog",
                columns: new[] { "ProjectId", "TemplateKey" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailNotificationLog_Status_UpdatedAt",
                table: "EmailNotificationLog",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_EmailNotificationLog_CorrelationKey",
                table: "EmailNotificationLog",
                column: "CorrelationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_EmailTemplates_TemplateKey",
                table: "EmailTemplates",
                column: "TemplateKey",
                unique: true);

            // Note: محتوى القوالب الأولي يأتي من EmailTemplateSeeds (بيانات في القاعدة لا كود في الخدمات)،
            // والمعرّف والتواريخ من قيم القاعدة الافتراضية (NEWID / SYSUTCDATETIME).
            foreach (var seed in EmailTemplateSeeds.All)
            {
                migrationBuilder.InsertData(
                    table: "EmailTemplates",
                    columns: new[] { "TemplateKey", "NameAr", "Subject", "BodyHtml", "IsEnabled" },
                    values: new object[] { seed.Key, seed.NameAr, seed.Subject, seed.BodyHtml, true });
            }

            // إعدادات التذكير الأسبوعي الافتراضية في جدول الإعدادات القائم (app_settings) — لا تُكتب فوق قيمة موجودة.
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.app_settings', N'U') IS NOT NULL
                BEGIN
                  IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'email_weekly_reminder_enabled')
                    INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'email_weekly_reminder_enabled', N'true');
                  IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'email_weekly_reminder_day')
                    INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'email_weekly_reminder_day', N'0');
                  IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE setting_key = N'email_weekly_reminder_time')
                    INSERT INTO dbo.app_settings (setting_key, setting_value) VALUES (N'email_weekly_reminder_time', N'08:00');
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.app_settings', N'U') IS NOT NULL
                  DELETE FROM dbo.app_settings WHERE setting_key IN (N'email_weekly_reminder_enabled', N'email_weekly_reminder_day', N'email_weekly_reminder_time');
                """);

            migrationBuilder.DropTable(
                name: "EmailNotificationLog");

            migrationBuilder.DropTable(
                name: "EmailTemplates");
        }
    }
}
