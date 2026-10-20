using System.Text.Json;
using ProjectTracker.Web.Data;
using Microsoft.Data.SqlClient;

namespace ProjectTracker.Web.Helpers;

public enum SqlKind { Text, Guid, Date, Int }

/// <summary>Whitelisted columns for a partial UPDATE: column names come from code, never from user input.</summary>
public sealed record SetColumn(string Column, SqlKind Kind, int Size = -1);

public static class SqlSet
{
    /// <summary>SET clauses and parameters for every whitelisted column present in <paramref name="value"/>.</summary>
    public static (List<string> Sets, List<SqlParameter> Parameters) Build(IEnumerable<SetColumn> columns, IReadOnlyDictionary<string, object?> value)
    {
        var sets = new List<string>();
        var parameters = new List<SqlParameter>();
        foreach (var c in columns)
        {
            if (!value.TryGetValue(c.Column, out var v)) continue;
            var name = $"@set_{c.Column}";
            var text = v as string;
            parameters.Add(c.Kind switch
            {
                SqlKind.Guid => SqlDb.Guid(name, text),
                SqlKind.Date => SqlDb.Date(name, text),
                SqlKind.Int => SqlDb.Int(name, v is int i ? i : null),
                _ => SqlDb.Text(name, text, c.Size),
            });
            sets.Add($"{c.Column} = @set_{c.Column}");
        }
        return (sets, parameters);
    }

    /// <summary>A view model as a name → JSON value lookup (used to record before/after values in the audit log).</summary>
    public static Dictionary<string, JsonElement> AsLookup(object vm)
    {
        var element = JsonSerializer.SerializeToElement(vm);
        return element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
