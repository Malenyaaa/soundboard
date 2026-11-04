using System.Text.RegularExpressions;
using Soundboard.Core.Config;
using Soundboard.Core.Hotkeys;
using Soundboard.Core.Text;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class LanguageTests
{
    [TestCleanup]
    public void BackToGerman() => L.SetLanguage(L.German);

    [TestMethod]
    public void Both_languages_have_exactly_the_same_keys()
    {
        var en = L.Keys(L.English);
        var de = L.Keys(L.German);
        Assert.IsTrue(en.Count > 300, $"only {en.Count} texts found – are the resources embedded?");
        var onlyEn = en.Except(de).ToList();
        var onlyDe = de.Except(en).ToList();
        Assert.AreEqual(0, onlyEn.Count, "missing in German: " + string.Join(", ", onlyEn));
        Assert.AreEqual(0, onlyDe.Count, "missing in English: " + string.Join(", ", onlyDe));
    }

    [TestMethod]
    public void Every_text_is_filled_and_has_the_same_placeholders_in_both_languages()
    {
        var placeholder = new Regex(@"\{(\d+)(?:[:,][^}]*)?\}");
        foreach (var key in L.Keys(L.English))
        {
            var en = L.Get(key, L.English);
            var de = L.Get(key, L.German);
            Assert.IsFalse(string.IsNullOrWhiteSpace(en), $"{key}: English is empty");
            Assert.IsFalse(string.IsNullOrWhiteSpace(de), $"{key}: German is empty");
            var pe = placeholder.Matches(en).Select(m => m.Groups[1].Value).Distinct().Order().ToList();
            var pd = placeholder.Matches(de).Select(m => m.Groups[1].Value).Distinct().Order().ToList();
            CollectionAssert.AreEqual(pe, pd, $"{key}: placeholders differ (en {string.Join(",", pe)} / de {string.Join(",", pd)})");
            if (key.StartsWith("Format.DateTime", StringComparison.Ordinal)) continue; // a date pattern, not a format string
            var args = Enumerable.Range(0, 4).Select(i => (object)(i + 1)).ToArray();
            _ = string.Format(L.CultureOf(L.English), en, args); // throws on a broken format string
            _ = string.Format(L.CultureOf(L.German), de, args);
        }
    }

    [TestMethod]
    public void Counted_texts_come_in_pairs()
    {
        var keys = L.Keys(L.English);
        foreach (var k in keys.Where(k => k.EndsWith(".One", StringComparison.Ordinal)))
            Assert.IsTrue(keys.Contains(k[..^4] + ".Other"), $"{k} has no .Other");
        foreach (var k in keys.Where(k => k.EndsWith(".Other", StringComparison.Ordinal)))
            Assert.IsTrue(keys.Contains(k[..^6] + ".One"), $"{k} has no .One");
    }

    [TestMethod]
    public void Every_key_used_in_the_code_exists()
    {
        var root = RepoRoot();
        var keys = L.Keys(L.English);
        var patterns = new[]
        {
            new Regex(@"\bL\.(?:Get|F)\(""([A-Za-z0-9]+\.[A-Za-z0-9.]+)"""),
            new Regex(@"\bTF?\(""([A-Za-z0-9]+\.[A-Za-z0-9.]+)"""),
            new Regex(@"\{t:T ([A-Za-z0-9.]+)\}"),
            new Regex(@"""@(Key\.[A-Za-z0-9]+)"""),
        };
        var count = new Regex(@"\bL\.Count\(""([A-Za-z0-9]+\.[A-Za-z0-9.]+)""");
        var missing = new List<string>();
        int found = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (var p in patterns)
                foreach (Match m in p.Matches(text))
                {
                    found++;
                    if (!keys.Contains(m.Groups[1].Value)) missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
                }
            foreach (Match m in count.Matches(text))
            {
                found++;
                if (!keys.Contains(m.Groups[1].Value + ".One") || !keys.Contains(m.Groups[1].Value + ".Other"))
                    missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}.One/.Other");
            }
        }
        Assert.IsTrue(found > 300, $"only {found} uses found – did the search miss the sources?");
        Assert.AreEqual(0, missing.Count, "texts used but not defined: " + string.Join("; ", missing));
    }

    [TestMethod]
    public void Typography_fits_each_language()
    {
        foreach (var key in L.Keys(L.English))
        {
            var en = L.Get(key, L.English);
            var de = L.Get(key, L.German);
            Assert.IsFalse(en.Contains('"') || de.Contains('"'), $"{key}: straight double quote");
            Assert.IsFalse(en.Contains("...") || de.Contains("..."), $"{key}: three dots instead of …");
            Assert.IsFalse(en.Contains('„'), $"{key}: German quote in English");
            Assert.IsFalse(de.Contains('”'), $"{key}: English closing quote in German");
            Assert.IsFalse(Regex.IsMatch(en, @"\w'\w"), $"{key}: straight apostrophe in English");
            Assert.IsFalse(Regex.IsMatch(en, @"\d %"), $"{key}: English percent takes no space");
        }
    }

    [TestMethod]
    public void Switching_changes_the_texts_at_once_and_says_so()
    {
        L.SetLanguage(L.English);
        int raised = 0;
        EventHandler h = (_, _) => raised++;
        L.Changed += h;
        try
        {
            Assert.AreEqual("Stop", L.Get("Main.Stop"));
            Assert.AreEqual("Settings", L.Get("Settings.Title"));
            Assert.AreEqual("Ctrl+Alt+Num 1", HotkeyGesture.Parse("Ctrl+Alt+NumPad1")!.Value.Display);
            Assert.AreEqual("80%", L.Percent(0.8));

            Assert.IsTrue(L.SetLanguage(L.German));
            Assert.AreEqual(1, raised);
            Assert.AreEqual("Einstellungen", L.Get("Settings.Title"));
            Assert.AreEqual("Strg+Alt+Num 1", HotkeyGesture.Parse("Ctrl+Alt+NumPad1")!.Value.Display);
            Assert.AreEqual("80 %", L.Percent(0.8));

            Assert.IsFalse(L.SetLanguage("DE"), "same language again: no event");
            Assert.AreEqual(1, raised);
        }
        finally
        {
            L.Changed -= h;
        }
    }

    [TestMethod]
    public void Numbers_dates_and_counts_follow_the_language()
    {
        var t = new DateTime(2026, 11, 4, 14, 30, 0);
        L.SetLanguage(L.English);
        Assert.AreEqual("4 Nov 2026, 14:30", L.DateTime(t));
        Assert.AreEqual("Keys: 1 active", L.Count("Status.Keys", 1));
        Assert.AreEqual("Keys: 3 active", L.Count("Status.Keys", 3));
        Assert.AreEqual("small (20 ms)", Audio.BufferSizes.Display(Audio.BufferSize.Small));
        L.SetLanguage(L.German);
        Assert.AreEqual("04.11.2026, 14:30", L.DateTime(t));
        Assert.AreEqual("klein (20 ms)", Audio.BufferSizes.Display(Audio.BufferSize.Small));
    }

    [TestMethod]
    public void Unknown_language_or_key_does_not_break()
    {
        Assert.AreEqual(L.English, L.Normalize("fr"));
        Assert.AreEqual(L.English, L.Normalize(null));
        Assert.AreEqual(L.German, L.Normalize("de"));
        Assert.AreEqual("[Gibt.Es.Nicht]", L.Get("Gibt.Es.Nicht"));
    }

    [TestMethod]
    public void New_profile_starts_in_English_old_profile_keeps_German()
    {
        Assert.AreEqual(L.English, new Profile().Language);

        using var dir = new TempDir();
        var path = dir.File("config.json");
        File.WriteAllText(path, """{ "Version": 3, "Tiles": [] }""");
        var old = ProfileStore.Load(path).Profile;
        Assert.AreEqual(L.German, old.Language, "whoever used 0.10 keeps German");
        Assert.AreEqual(Profile.CurrentVersion, old.Version);

        ProfileStore.Save(path, new Profile { Language = L.English });
        Assert.AreEqual(L.English, ProfileStore.Load(path).Profile.Language, "a chosen English stays English");

        File.WriteAllText(path, """{ "Version": 4, "Language": "klingonisch", "Tiles": [] }""");
        Assert.AreEqual(L.English, ProfileStore.Load(path).Profile.Language);

        File.WriteAllText(path, """{ "Version": 4, "Tiles": [] }""");
        Assert.AreEqual(L.English, ProfileStore.Load(path).Profile.Language);
    }

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Soundboard.sln"))) return d.FullName;
        Assert.Inconclusive("Repository root not found.");
        return "";
    }
}
