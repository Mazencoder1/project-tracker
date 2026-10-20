using System.Globalization;

namespace ProjectTracker.Web.Data;

/// <summary>
/// One result row, read exactly like the Node <c>mssql</c> recordset the original code used:
/// uniqueidentifier values are upper-case strings, and date/datetime2 values render as UTC ISO strings.
/// </summary>
public sealed class Row
{
    private readonly Dictionary<string, object?> _values;

    public Row(Dictionary<string, object?> values) => _values = values;

    public object? this[string key] => _values.TryGetValue(key, out var v) ? v : null;

    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>JS <c>String(value)</c> for non-null values; null stays null.</summary>
    public string? Str(string key) => this[key] switch
    {
        null => null,
        Guid g => g.ToString().ToUpperInvariant(),
        DateTime d => Iso(d),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var o => o.ToString(),
    };

    public double? Num(string key) => this[key] switch
    {
        null => null,
        double d => double.IsFinite(d) ? d : null,
        float f => double.IsFinite(f) ? f : null,
        decimal m => (double)m,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
        IConvertible c => Convert.ToDouble(c, CultureInfo.InvariantCulture),
        _ => null,
    };

    public int? Int(string key) => Num(key) is { } n ? (int)n : null;

    /// <summary>yyyy-MM-dd from a date/datetime column (calendar parts as stored).</summary>
    public string? Date(string key) => this[key] switch
    {
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string s when s.Length >= 10 => s[..10],
        _ => null,
    };

    /// <summary>Full UTC ISO timestamp (the JS <c>toISOString()</c> shape), or null.</summary>
    public string? Iso(string key) => this[key] is DateTime d ? Iso(d) : Str(key);

    public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The row as a JSON-ready dictionary (used for endpoints that return table rows untouched).</summary>
    public Dictionary<string, object?> ToJson()
    {
        var result = new Dictionary<string, object?>(_values.Count);
        foreach (var (key, value) in _values) result[key] = ToJsonValue(value);
        return result;
    }

    public static object? ToJsonValue(object? value) => value switch
    {
        null => null,
        Guid g => g.ToString().ToUpperInvariant(),
        DateTime d => Iso(d),
        decimal m => (double)m,
        _ => value,
    };
}
