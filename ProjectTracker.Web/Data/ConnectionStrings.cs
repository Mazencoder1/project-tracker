using Microsoft.Data.SqlClient;
using ProjectTracker.Web.Services;

namespace ProjectTracker.Web.Data;

/// <summary>Builds the SQL Server connection string from the application settings.</summary>
public static class ConnectionStrings
{
    public static string Build(AppConfig config)
    {
        if (config.DbConnectionString is { } ready) return ready;

        if (string.IsNullOrEmpty(config.DbName))
            throw new InvalidOperationException("إعداد قاعدة البيانات ناقص: اضبط Database:Name (أو متغير البيئة DB_NAME) في appsettings أو متغيرات البيئة.");

        var address = config.DbInstance is { } instance && !config.DbServer.Contains('\\') ? $"{config.DbServer}\\{instance}" : config.DbServer;
        if (config.DbPort is { } port && !address.Contains(',')) address = $"{address},{port}";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = address,
            InitialCatalog = config.DbName,
            Encrypt = config.DbEncrypt,
            TrustServerCertificate = config.DbTrustServerCertificate,
            ApplicationName = "Project Tracker",
            MaxPoolSize = 10,
        };
        if (config.DbTrustedConnection)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            if (string.IsNullOrEmpty(config.DbUser) || string.IsNullOrEmpty(config.DbPassword))
                throw new InvalidOperationException("إعداد قاعدة البيانات ناقص: اضبط Database:User و Database:Password (أو DB_USER و DB_PASSWORD)، أو اتركهما فارغين لمصادقة ويندوز.");
            builder.UserID = config.DbUser;
            builder.Password = config.DbPassword;
        }
        return builder.ConnectionString;
    }
}
