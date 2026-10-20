using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ProjectTracker.Web.Data;

/// <summary>
/// Parameterised raw-SQL access on the same connection (and transaction) EF Core uses. The reporting
/// queries of the original system are SQL-heavy (window functions, multi-result batches), so they are
/// carried over verbatim instead of being re-expressed in LINQ — same statements, same results.
/// </summary>
public sealed class SqlDb(AppDbContext context)
{
    public AppDbContext Context => context;

    private async Task<DbCommand> CommandAsync(string sql, IEnumerable<SqlParameter>? parameters, CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        if (parameters != null) foreach (var p in parameters) command.Parameters.Add(p);
        return command;
    }

    /// <summary>All result sets of a batch, in order.</summary>
    public async Task<List<List<Row>>> QueryMultiAsync(string sql, IEnumerable<SqlParameter>? parameters = null, CancellationToken ct = default)
    {
        await using var command = await CommandAsync(sql, parameters, ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var sets = new List<List<Row>>();
        do
        {
            var rows = new List<Row>();
            while (await reader.ReadAsync(ct))
            {
                var values = new Dictionary<string, object?>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                    values[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(new Row(values));
            }
            sets.Add(rows);
        } while (await reader.NextResultAsync(ct));
        return sets;
    }

    public async Task<List<Row>> QueryAsync(string sql, IEnumerable<SqlParameter>? parameters = null, CancellationToken ct = default)
        => (await QueryMultiAsync(sql, parameters, ct))[0];

    public async Task<Row?> FirstAsync(string sql, IEnumerable<SqlParameter>? parameters = null, CancellationToken ct = default)
        => (await QueryAsync(sql, parameters, ct)).FirstOrDefault();

    /// <summary>Rows affected by the batch.</summary>
    public async Task<int> ExecuteAsync(string sql, IEnumerable<SqlParameter>? parameters = null, CancellationToken ct = default)
    {
        await using var command = await CommandAsync(sql, parameters, ct);
        return await command.ExecuteNonQueryAsync(ct);
    }

    // Parameter helpers, typed like the original mssql inputs.
    public static SqlParameter Guid(string name, string? value) =>
        new(name, SqlDbType.UniqueIdentifier) { Value = string.IsNullOrEmpty(value) ? DBNull.Value : System.Guid.Parse(value) };

    public static SqlParameter Text(string name, string? value, int size = -1) =>
        new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };

    public static SqlParameter Int(string name, int? value) =>
        new(name, SqlDbType.Int) { Value = value.HasValue ? value.Value : DBNull.Value };

    public static SqlParameter Date(string name, string? value) =>
        new(name, SqlDbType.Date)
        {
            Value = string.IsNullOrEmpty(value)
                ? DBNull.Value
                : DateTime.ParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

    /// <summary>SQL Server error number of a failed command (2627/2601 duplicate key, 547 constraint...), or 0.</summary>
    public static int SqlErrorNumber(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e is SqlException sql) return sql.Number;
        return 0;
    }
}
