using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectTracker.Web.Models;

// EF entities for the tables read through EF (see AppDbContext). Table and column names match the
// existing SQL Server schema (database/INSTALL-ALL-IN-ONE.sql); nothing here creates or alters a table.

[Table("profiles")]
public class Profile
{
    [Key, Column("id")] public Guid Id { get; set; }
    [Column("full_name"), MaxLength(100)] public string FullName { get; set; } = "";
    [Column("email"), MaxLength(150)] public string? Email { get; set; }
    [Column("role"), MaxLength(20)] public string? Role { get; set; }
    [Column("department"), MaxLength(100)] public string? Department { get; set; }
    [Column("password_hash"), MaxLength(255)] public string? PasswordHash { get; set; }
    [Column("created_at")] public DateTime? CreatedAt { get; set; }
    [Column("status"), MaxLength(20)] public string Status { get; set; } = "active";
    [Column("employee_number"), MaxLength(50)] public string? EmployeeNumber { get; set; }
    [Column("job_title"), MaxLength(200)] public string? JobTitle { get; set; }
    [Column("mobile_no"), MaxLength(30)] public string? MobileNo { get; set; }
    [Column("main_department"), MaxLength(200)] public string? MainDepartment { get; set; }
    [Column("section_name"), MaxLength(200)] public string? SectionName { get; set; }
    [Column("extension_number"), MaxLength(20)] public string? ExtensionNumber { get; set; }
    [Column("manager_name"), MaxLength(200)] public string? ManagerName { get; set; }
    [Column("manager_number"), MaxLength(50)] public string? ManagerNumber { get; set; }
    [Column("authorized_manager_name"), MaxLength(200)] public string? AuthorizedManagerName { get; set; }
    [Column("authorized_manager_number"), MaxLength(50)] public string? AuthorizedManagerNumber { get; set; }
    [Column("directory_synced_at")] public DateTime? DirectorySyncedAt { get; set; }
}

[Table("app_settings")]
public class AppSettingRow
{
    [Key, Column("setting_key"), MaxLength(100)] public string SettingKey { get; set; } = "";
    [Column("setting_value"), MaxLength(400)] public string SettingValue { get; set; } = "";
    [Column("updated_by")] public Guid? UpdatedBy { get; set; }
    [Column("updated_at")] public DateTime UpdatedAt { get; set; }
}

