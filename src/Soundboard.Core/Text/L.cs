using System.Collections;
using System.Globalization;
using System.Resources;

namespace Soundboard.Core.Text;

/// <summary>
/// The texts of the program in English and German. Every visible text lives in Text/En.resx
/// and Text/De.resx under the same key; nothing is put together from pieces in the code.
/// English is the default; the language chosen in the settings is stored in the profile.
/// Both files are ordinary embedded resources of this assembly (no satellite assemblies),
/// so the single-file EXE always carries both languages.
/// </summary>
public static class L
{
    public const string English = "en";
    public const string German = "de";

    public static IReadOnlyList<string> Languages { get; } = [English, German];

    private static readonly ResourceManager En = new("Soundboard.Core.Text.En", typeof(L).Assembly);
    private static readonly ResourceManager De = new("Soundboard.Core.Text.De", typeof(L).Assembly);
    private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo DeCulture = CultureInfo.GetCultureInfo("de-DE");

    private static volatile string _language = English;

    /// <summary>"en" or "de".</summary>
    public static string Language => _language;

    /// <summary>Culture for numbers and dates in the current language.</summary>
    public static CultureInfo Culture => CultureOf(_language);

    public static CultureInfo CultureOf(string language) => language == German ? DeCulture : EnCulture;

    /// <summary>Raised after the language changed (on the thread that changed it).</summary>
    public static event EventHandler? Changed;

    /// <summary>"de" stays "de"; anything else becomes "en".</summary>
    public static string Normalize(string? language) =>
        string.Equals(language, German, StringComparison.OrdinalIgnoreCase) ? German : English;

    /// <summary>Switches the language. Returns false if it was already set.</summary>
    public static bool SetLanguage(string? language)
    {
        var lang = Normalize(language);
        if (lang == _language) return false;
        _language = lang;
        Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }

    /// <summary>The text for <paramref name="key"/> in the current language.</summary>
    public static string Get(string key) => Get(key, _language);

    public static string Get(string key, string language)
    {
        var rm = Normalize(language) == German ? De : En;
        return rm.GetString(key, CultureInfo.InvariantCulture)
            ?? En.GetString(key, CultureInfo.InvariantCulture)
            ?? "[" + key + "]";
    }

    /// <summary>The text for <paramref name="key"/> with its placeholders filled ({0}, {1:N0} …) in the current language.</summary>
    public static string F(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    /// <summary>
    /// Text with a number: <c>key.One</c> for exactly one, <c>key.Other</c> otherwise. The number is
    /// placeholder {0}; further arguments follow as {1}, {2} ….
    /// </summary>
    public static string Count(string key, long n, params object?[] args)
    {
        var all = new object?[args.Length + 1];
        all[0] = n;
        Array.Copy(args, 0, all, 1, args.Length);
        return string.Format(Culture, Get(n == 1 ? key + ".One" : key + ".Other"), all);
    }

    /// <summary>Date and time as shown in the window ("4 Nov 2026, 14:30" / "04.11.2026, 14:30").</summary>
    public static string DateTime(System.DateTime t) => t.ToString(Get("Format.DateTime"), Culture);

    /// <summary>Date and time with seconds, for the console report.</summary>
    public static string DateTimeSeconds(System.DateTime t) => t.ToString(Get("Format.DateTimeSeconds"), Culture);

    /// <summary>A share 0..1 as percent ("80%" / "80 %").</summary>
    public static string Percent(double share) => F("Format.Percent", Math.Round(share * 100));

    /// <summary>All keys of one language file (for the test that finds missing texts).</summary>
    public static IReadOnlyCollection<string> Keys(string language)
    {
        var rm = Normalize(language) == German ? De : En;
        var set = rm.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        if (set is null) return keys;
        foreach (DictionaryEntry e in set) keys.Add((string)e.Key);
        return keys;
    }
}
