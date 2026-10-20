using System.Text.Json;

namespace ProjectTracker.Web.Helpers;

/// <summary>
/// The parsed JSON object of a request, keeping JavaScript-style "unknown" values so the validation
/// rules (which distinguish missing, null, string and number) behave exactly as before.
/// </summary>
public sealed class JsonBody
{
    private readonly Dictionary<string, JsonElement> _props;

    private JsonBody(Dictionary<string, JsonElement> props) => _props = props;

    public static JsonBody Empty { get; } = new([]);

    /// <summary>The body as an object, or null when the body is missing, malformed or not an object.</summary>
    public static async Task<JsonBody?> ReadAsync(HttpRequest request)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(request.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var props = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject()) props[p.Name] = p.Value.Clone();
            return new JsonBody(props);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Like req.json(): malformed JSON throws (mapped to 400), a non-object body reads as empty.</summary>
    public static async Task<JsonBody> ReadOrThrowAsync(HttpRequest request)
    {
        using var doc = await JsonDocument.ParseAsync(request.Body);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return Empty;
        var props = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject()) props[p.Name] = p.Value.Clone();
        return new JsonBody(props);
    }

    public JsonElement? this[string key] => _props.TryGetValue(key, out var v) ? v : null;

    /// <summary>Property present (JSON null counts as present; JS undefined does not).</summary>
    public bool Has(string key) => _props.ContainsKey(key);

    public IEnumerable<string> Keys => _props.Keys;

    /// <summary>The value when it is a JSON string, else null.</summary>
    public string? String(string key) => Js.StringOrNull(this[key]);

    /// <summary>All properties as loosely typed values (string / long / double / bool / null), for the validation rules.</summary>
    public Dictionary<string, object?> ToDictionary() => _props.ToDictionary(p => p.Key, p => ToClr(p.Value));

    /// <summary>Raw JSON value → CLR object (string/number/bool/null) for parameters passed straight to SQL.</summary>
    public object? Raw(string key) => this[key] is { } v ? ToClr(v) : null;

    public static object? ToClr(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => Js.SafeString(v),
        JsonValueKind.Number => v.TryGetInt64(out var l) ? l : v.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
