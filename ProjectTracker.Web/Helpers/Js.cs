using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTracker.Web.Helpers;

/// <summary>JavaScript-flavoured helpers the validation rules (ported from the Node.js system) rely on.</summary>
public static partial class Js
{
    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex UuidRegex();

    public static bool IsUuid(string? value) => value is not null && UuidRegex().IsMatch(value);

    /// <summary>Math.round: halves round toward +infinity (Math.Round in .NET rounds to even).</summary>
    public static int Round(double value) => (int)Math.Floor(value + 0.5);

    /// <summary>parseInt(value, 10): leading integer of the text, or null when there is none or it does not fit an int.</summary>
    public static int? ParseInt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var span = value.AsSpan().TrimStart();
        var i = 0;
        if (i < span.Length && (span[i] == '+' || span[i] == '-')) i++;
        var digitsStart = i;
        while (i < span.Length && span[i] is >= '0' and <= '9') i++;
        if (i == digitsStart) return null;
        return long.TryParse(span[..i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)
            && result is >= int.MinValue and <= int.MaxValue
            ? (int)result
            : null;
    }

    /// <summary>String(number): whole numbers without a decimal point (5, not 5.0).</summary>
    public static string NumberToString(double value) =>
        double.IsInteger(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Arabic-Indic (٠-٩) and Persian (۰-۹) digits → Latin digits.</summary>
    public static string NormalizeDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '٠' and <= '٩') chars[i] = (char)('0' + (chars[i] - '٠'));
            else if (chars[i] is >= '۰' and <= '۹') chars[i] = (char)('0' + (chars[i] - '۰'));
        }
        return new string(chars);
    }

    /// <summary>Trimmed string for JSON strings and numbers, null for anything else or empty (validation.ts text()).</summary>
    public static string? Text(JsonElement? value)
    {
        if (value is not { } v) return null;
        string? s = v.ValueKind switch
        {
            JsonValueKind.String => SafeString(v),
            JsonValueKind.Number => NumberToString(v.GetDouble()),
            _ => null,
        };
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>The value when it is a JSON string, else null (JS: typeof v === 'string').</summary>
    public static string? StringOrNull(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.String } v ? SafeString(v) : null;

    /// <summary>
    /// GetString() throws InvalidOperationException for text holding a lone UTF-16 surrogate (a JSON escape such as
    /// backslash-u-d800). That is bad input, not a server fault, so it is reported as a JsonException (→ 400, not 500).
    /// </summary>
    public static string? SafeString(JsonElement value)
    {
        try { return value.GetString(); }
        catch (InvalidOperationException) { throw new JsonException("Invalid Unicode text."); }
    }

    /// <summary>YYYY-MM-DD strictly valid calendar date.</summary>
    public static bool IsIsoDate(string? value) =>
        value is { Length: 10 }
        && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static string Today() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
