using ProjectTracker.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ProjectTracker.Web.Data;

/// <summary>
/// EF Core context over the existing Project Tracker database. It maps only the tables read through EF
/// (profiles, app_settings and the e-mail tables); every other table is queried with <see cref="SqlDb"/>.
/// Schema changes are made by database/INSTALL-ALL-IN-ONE.sql, never by EF (no EnsureCreated).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<AppSettingRow> AppSettings => Set<AppSettingRow>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmailNotificationLog> EmailNotificationLog => Set<EmailNotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The two e-mail tables (migrations in Migrations/EmailNotifications) — the model is shared with the design-time context.
        EmailSchema.Configure(modelBuilder);

        // Database-side defaults (DEFAULT NEWID()/GETDATE()/SYSUTCDATETIME()) stay authoritative:
        // EF omits these columns on insert and reads the generated values back.
        modelBuilder.Entity<AppSettingRow>(e => e.Property(x => x.UpdatedAt).HasDefaultValueSql("GETDATE()"));
        modelBuilder.Entity<Profile>(e =>
        {
            e.Property(x => x.Id).HasDefaultValueSql("NEWID()");
            e.Property(x => x.Role).HasDefaultValueSql("'pm'");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("GETDATE()");
            e.Property(x => x.Status).HasDefaultValueSql("'active'");
        });
    }
}
