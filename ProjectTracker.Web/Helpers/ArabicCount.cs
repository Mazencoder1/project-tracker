namespace ProjectTracker.Web.Helpers;

/// <summary>Arabic counted-noun forms (port of lib/arabicCount.ts).</summary>
public static class ArabicCount
{
    private sealed record Forms(string One, string Two, string Few, string Many, string Single);

    private static readonly Dictionary<string, Forms> Units = new()
    {
        ["week"] = new("أسبوع", "أسبوعان", "أسابيع", "أسبوعًا", "واحد"),
        ["day"] = new("يوم", "يومان", "أيام", "يومًا", "واحد"),
        ["project"] = new("مشروع", "مشروعان", "مشاريع", "مشروعًا", "واحد"),
        ["milestone"] = new("مرحلة", "مرحلتان", "مراحل", "مرحلة", "واحدة"),
        ["attempt"] = new("محاولة", "محاولتان", "محاولات", "محاولة", "واحدة"),
    };

    /// <summary>«أسبوع واحد» · «أسبوعان» · «5 أسابيع» · «12 أسبوعًا» · «0 أسابيع».</summary>
    public static string Format(double value, string unit)
    {
        var n = Math.Max(0, Js.Round(double.IsFinite(value) ? value : 0));
        var f = Units[unit];
        if (n == 1) return $"{f.One} {f.Single}";
        if (n == 2) return f.Two;
        if (n == 0) return $"0 {f.Few}";
        var tail = n % 100;
        if (tail is >= 3 and <= 10) return $"{n} {f.Few}";
        if (tail >= 11) return $"{n} {f.Many}";
        return $"{n} {f.One}";
    }
}
