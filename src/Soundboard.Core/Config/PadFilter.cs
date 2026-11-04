using System.Globalization;

namespace Soundboard.Core.Config;

/// <summary>
/// The search above the pads: every word typed must appear in the pad name or in its key
/// ("air", "num 3", "strg alt"). Case and accents do not matter; an empty search shows all.
/// </summary>
public static class PadFilter
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;

    public static bool IsActive(string? query) => !string.IsNullOrWhiteSpace(query);

    public static bool Matches(string? name, string? keyText, string? query)
    {
        if (!IsActive(query)) return true;
        foreach (var word in query!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            bool found = (name is not null && Compare.IndexOf(name, word, Loose) >= 0)
                || (keyText is not null && Compare.IndexOf(keyText, word, Loose) >= 0);
            if (!found) return false;
        }
        return true;
    }
}
