namespace Soundboard.Core.Hotkeys;

/// <summary>
/// Texts for a key cap that has little room. The key itself is never cut: when the whole
/// combination does not fit, the modifiers go from the front, one at a time, and an ellipsis
/// says that something is missing ("…Umschalt+F12"). The full text belongs in the tooltip.
/// </summary>
public static class KeyCapText
{
    public const string Ellipsis = "…";

    /// <summary>From the full text to the shortest: "Strg+Alt+F12", "…Alt+F12", "…F12".</summary>
    public static IReadOnlyList<string> Variants(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0) return [];
        var list = new List<string> { string.Join("+", parts) };
        for (int i = 1; i < parts.Count; i++) list.Add(Ellipsis + string.Join("+", parts.Skip(i)));
        return list;
    }

    public static IReadOnlyList<string> Variants(HotkeyGesture gesture) => Variants(gesture.DisplayParts);

    /// <summary>The longest variant that fits, measured by <paramref name="width"/>; the shortest if none does.</summary>
    public static string Fit(IReadOnlyList<string> variants, double available, Func<string, double> width)
    {
        if (variants.Count == 0) return "";
        foreach (var v in variants)
            if (width(v) <= available + 0.5) return v;
        return variants[^1];
    }
}
