using ProjectTracker.Web.Helpers;
using ProjectTracker.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ProjectTracker.Web.Data;

/// <summary>
/// The model of the two e-mail tables, shared by the runtime context (AppDbContext) and by the design-time
/// context that owns their migration (EmailSchemaDbContext). Keeping it in one place guarantees the migration
/// describes exactly the tables the application reads and writes.
/// </summary>
public static class EmailSchema
{
    public const string TemplatesUniqueKey = "UQ_EmailTemplates_TemplateKey";
    public const string LogUniqueKey = "UQ_EmailNotificationLog_CorrelationKey";

    public static void Configure(ModelBuilder b)
    {
        b.Entity<EmailTemplate>(e =>
        {
            e.Property(x => x.Id).HasDefaultValueSql("NEWID()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Fields.SqlUtcNow);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql(Fields.SqlUtcNow);
            e.Property(x => x.BodyHtml).HasColumnType("nvarchar(max)");
            e.Property(x => x.MessageText).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.TemplateKey).IsUnique().HasDatabaseName(TemplatesUniqueKey);
        });

        b.Entity<EmailNotificationLog>(e =>
        {
            e.Property(x => x.Id).HasDefaultValueSql("NEWID()");
            e.Property(x => x.Status).HasDefaultValue(EmailLogStatus.Pending);
            e.Property(x => x.AttemptCount).HasDefaultValue(0);
            e.Property(x => x.CreatedAt).HasDefaultValueSql(Fields.SqlUtcNow);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql(Fields.SqlUtcNow);
            e.HasIndex(x => x.CorrelationKey).IsUnique().HasDatabaseName(LogUniqueKey);
            e.HasIndex(x => new { x.ProjectId, x.TemplateKey }).HasDatabaseName("IX_EmailNotificationLog_Project_Template");
            e.HasIndex(x => new { x.Status, x.UpdatedAt }).HasDatabaseName("IX_EmailNotificationLog_Status_UpdatedAt");
            e.ToTable(t => t.HasCheckConstraint("CK_EmailNotificationLog_Status", "[Status] IN (N'Pending', N'Sent', N'Failed')"));
        });
    }
}

/// <summary>
/// Design-time only: the context `dotnet ef migrations` uses, so the migration contains the two new tables and
/// nothing of the existing schema. It has its own history table and is never created at run time.
/// </summary>
public sealed class EmailSchemaDbContext(DbContextOptions<EmailSchemaDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__EFMigrationsHistory_EmailNotifications";

    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmailNotificationLog> EmailNotificationLog => Set<EmailNotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => EmailSchema.Configure(modelBuilder);
}

/// <summary>Lets `dotnet ef` build EmailSchemaDbContext without starting the web host (and without any secrets).</summary>
public sealed class EmailSchemaDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<EmailSchemaDbContext>
{
    public EmailSchemaDbContext CreateDbContext(string[] args)
    {
        // Note: نص اتصال شكلي لتوليد الترحيل/السكربت فقط؛ لا يُفتح أي اتصال ولا يحوي سرًا. للتطبيق الفعلي استعمل
        // السكربت المُولَّد (database/migrations) أو مرّر نص اتصال في المعامل الأول لـ dotnet ef database update.
        var connection = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "Server=.;Database=ProjectTracker;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<EmailSchemaDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable(EmailSchemaDbContext.HistoryTable).MigrationsAssembly(typeof(EmailSchemaDbContext).Assembly.GetName().Name))
            .Options;
        return new EmailSchemaDbContext(options);
    }
}
