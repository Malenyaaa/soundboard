using System.Text.Json;
using System.Text.Json.Serialization;
using Soundboard.Core.Audio;

namespace Soundboard.Core.Config;

/// <summary>One tile on the board.</summary>
public sealed class TileConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>File name inside the sound folder (no directory part).</summary>
    public string FileName { get; set; } = "";

    /// <summary>Tile volume 0..1.</summary>
    public float Volume { get; set; } = 1f;

    /// <summary>Background colour as #RRGGBB.</summary>
    public string Color { get; set; } = "#DDE7F5";

    /// <summary>
    /// The colour was picked for this pad (right-click → Colour). False = automatic: the pad takes
    /// the colour of its place in the order, and moving or removing pads recolours it. Since version 5.
    /// </summary>
    public bool ColorChosen { get; set; }

    /// <summary>Global hotkey as token text (e.g. "NumPad1"); null = none.</summary>
    public string? Hotkey { get; set; }

    /// <summary>Background picture: file name inside the picture folder (no directory part); null = none. Since version 3.</summary>
    public string? Image { get; set; }
}

/// <summary>User settings: tiles and the chosen monitor device. Stored as JSON.</summary>
public sealed class Profile
{
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Endpoint ID of the monitor (headphones). Null = not chosen yet.</summary>
    public string? MonitorDeviceId { get; set; }

    /// <summary>Last known name of the monitor device, for messages when it is missing.</summary>
    public string? MonitorDeviceName { get; set; }

    public bool MonitorEnabled { get; set; } = true;

    /// <summary>Master volume for sounds, 0..1.</summary>
    public float MasterVolume { get; set; } = 0.8f;

    public OverlapMode Overlap { get; set; } = OverlapMode.StopPrevious;

    /// <summary>Endpoint ID of the microphone. Null = not chosen yet.</summary>
    public string? MicDeviceId { get; set; }

    /// <summary>Last known name of the microphone, for messages when it is missing.</summary>
    public string? MicDeviceName { get; set; }

    /// <summary>Microphone level inside the app, 0..2 (1 = unchanged). Not the Windows volume.</summary>
    public float MicVolume { get; set; } = 1f;

    /// <summary>Buffer size of the microphone path.</summary>
    public BufferSize Buffer { get; set; } = BufferSize.Medium;

    /// <summary>Mute the microphone while a sound plays. Default off.</summary>
    public bool BlockVoiceWhileSound { get; set; }

    /// <summary>Send microphone + sounds to the virtual cable when it is there.</summary>
    public bool CableEnabled { get; set; } = true;

    /// <summary>The setup panel was shown once (first start).</summary>
    public bool SetupSeen { get; set; }

    /// <summary>Microphone that was chosen in Discord before the cable (free text, for the way back).</summary>
    public string? DiscordPreviousMic { get; set; }

    /// <summary>The user confirmed the Discord voice settings (noise suppression etc.).</summary>
    public bool DiscordSettingsDone { get; set; }

    /// <summary>The hint "closing keeps the app in the tray" was shown once.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>Global hotkey for "stop all" as token text; null = none.</summary>
    public string? StopAllHotkey { get; set; } = "Pause";

    /// <summary>What the key of a playing pad does: "Stop" (default) or "Restart" (play from the start). Clicks always stop.</summary>
    public string SameKeyAgain { get; set; } = SameKeyModes.Stop;

    /// <summary>Global hotkey that switches the microphone mute on and off; null = none (default).</summary>
    public string? MuteMicHotkey { get; set; }

    public List<TileConfig> Tiles { get; set; } = [];

    // ---- design (since version 2); stored as text so an unknown value never breaks loading

    /// <summary>"System" (follow Windows), "Hell" or "Dunkel".</summary>
    public string Appearance { get; set; } = AppearanceModes.System;

    /// <summary>Accent preset id (see Design.AccentPalette) or "eigene" for <see cref="CustomAccent"/>.</summary>
    public string AccentId { get; set; } = Design.AccentPalette.DefaultId;

    /// <summary>Free accent colour as #RRGGBB, used when <see cref="AccentId"/> is "eigene".</summary>
    public string? CustomAccent { get; set; }

    /// <summary>"Bunt" (every pad its own colour) or "Akzent" (all pads in the accent colour).</summary>
    public string PadColors { get; set; } = PadColorModes.Colorful;

    /// <summary>"Large" (default) or "Compact": compact pads are smaller, so many fit on one screen.</summary>
    public string PadSize { get; set; } = PadSizes.Large;

    /// <summary>Short transitions (pad lighting up). Windows' own "animation effects" setting is respected as well.</summary>
    public bool Animations { get; set; } = true;

    /// <summary>Language of the interface: "en" or "de". New profiles start in English; profiles from before version 4 keep German.</summary>
    public string Language { get; set; } = Text.L.English;

    public const int CurrentVersion = 5;

    /// <summary>
    /// Brings an older or hand-edited profile up to date: missing design values get their
    /// defaults, unknown values are replaced. Returns true if something changed.
    /// </summary>
    public bool Migrate()
    {
        bool changed = false;
        if (Version < 4)
        {
            // Version 4: English became the default. Whoever used the program before keeps German.
            Language = Text.L.German;
            // The Wada colours gave way to plain ones: old presets and pad colours move to the nearest new one.
            if (Design.AccentPalette.Migrate(AccentId) is { } accent) AccentId = accent;
            foreach (var t in Tiles)
                if (t.Color is not null) t.Color = Design.PadPalette.Migrate(t.Color);
        }
        if (Version < 5)
        {
            // Version 5: pad colours can be automatic (by order) or chosen. A pad whose colour is not the
            // one of its place keeps it as chosen, so nothing changes colour with the update.
            for (int i = 0; i < Tiles.Count; i++)
                Tiles[i].ColorChosen = !Design.PadPalette.SameHex(Tiles[i].Color, Design.PadPalette.ForIndex(i));
        }
        if (Version < CurrentVersion)
        {
            Version = CurrentVersion;
            changed = true;
        }
        if (Language != Text.L.Normalize(Language))
        {
            Language = Text.L.Normalize(Language);
            changed = true;
        }
        if (!AppearanceModes.All.Contains(Appearance))
        {
            Appearance = AppearanceModes.System;
            changed = true;
        }
        if (AccentId != Design.AccentPalette.CustomId && Design.AccentPalette.Find(AccentId) is null)
        {
            AccentId = Design.AccentPalette.Migrate(AccentId) ?? Design.AccentPalette.DefaultId;
            changed = true;
        }
        if (AccentId == Design.AccentPalette.CustomId && !Design.Rgb.TryParse(CustomAccent, out _))
        {
            AccentId = Design.AccentPalette.DefaultId;
            changed = true;
        }
        if (PadSize is not (PadSizes.Large or PadSizes.Compact))
        {
            PadSize = PadSizes.Large;
            changed = true;
        }
        if (SameKeyAgain is not (SameKeyModes.Stop or SameKeyModes.Restart))
        {
            SameKeyAgain = SameKeyModes.Stop;
            changed = true;
        }
        if (PadColors is not (PadColorModes.Colorful or PadColorModes.Accent))
        {
            PadColors = PadColorModes.Colorful;
            changed = true;
        }
        foreach (var t in Tiles)
        {
            if (!Design.Rgb.TryParse(t.Color, out _))
            {
                t.Color = Design.PadPalette.ForIndex(Tiles.IndexOf(t));
                changed = true;
            }
            // Only a plain file name inside the picture folder; anything else (folder parts, "..") is dropped.
            if (t.Image is not null && !Images.ImageFiles.IsPlainFileName(t.Image))
            {
                t.Image = null;
                changed = true;
            }
        }
        return changed;
    }
}

public static class AppearanceModes
{
    public const string System = "System";
    public const string Light = "Hell";
    public const string Dark = "Dunkel";
    public static readonly string[] All = [System, Light, Dark];
}

public static class PadSizes
{
    public const string Large = "Large";
    public const string Compact = "Compact";
}

public static class SameKeyModes
{
    public const string Stop = "Stop";
    public const string Restart = "Restart";
}

public static class PadColorModes
{
    public const string Colorful = "Bunt";
    public const string Accent = "Akzent";
}

/// <summary>Result of loading a profile; <see cref="Problem"/> is a hint for the user if something went wrong.</summary>
public sealed record ProfileLoadResult(Profile Profile, string? Problem);

/// <summary>Reads and writes the profile JSON. Writes atomically (temp file + move).</summary>
public static class ProfileStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ProfileLoadResult Load(string path)
    {
        if (!File.Exists(path)) return new ProfileLoadResult(new Profile(), null);
        try
        {
            var json = File.ReadAllText(path);
            var profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions) ?? new Profile();
            profile.Tiles ??= [];
            profile.Tiles.RemoveAll(t => t is null);
            profile.Appearance ??= AppearanceModes.System;
            profile.AccentId ??= Design.AccentPalette.DefaultId;
            profile.PadColors ??= PadColorModes.Colorful;
            profile.Language ??= Text.L.English;
            profile.Migrate();
            return new ProfileLoadResult(profile, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string? backup = null;
            try
            {
                backup = Path.Combine(Path.GetDirectoryName(path)!, $"config.defekt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(path, backup, overwrite: true);
            }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
            {
                backup = null;
            }
            var hint = backup is null
                ? Text.L.Get("Profile.Unreadable")
                : Text.L.F("Profile.UnreadableBackup", Path.GetFileName(backup));
            return new ProfileLoadResult(new Profile(), hint);
        }
    }

    /// <summary>Reads the profile without any side effect (no backup, no reset); null if it cannot be read.</summary>
    public static Profile? Peek(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), JsonOptions);
            if (profile is null) return null;
            profile.Tiles ??= [];
            profile.Tiles.RemoveAll(t => t is null);
            profile.Appearance ??= AppearanceModes.System;
            profile.AccentId ??= Design.AccentPalette.DefaultId;
            profile.PadColors ??= PadColorModes.Colorful;
            profile.Language ??= Text.L.English;
            profile.Migrate();
            return profile;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Save(string path, Profile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Where the app keeps its data: %LOCALAPPDATA%\Soundboard.</summary>
public sealed class AppPaths
{
    public AppPaths(string dataDir)
    {
        DataDir = dataDir;
        SoundsDir = Path.Combine(dataDir, "Sounds");
        ImagesDir = Path.Combine(dataDir, "Bilder");
        ProfileFile = Path.Combine(dataDir, "config.json");
        RecordingsDir = Path.Combine(dataDir, "Aufnahmen");
        BackupsDir = Path.Combine(dataDir, "Sicherungen");
        SnapshotFile = Path.Combine(dataDir, "audio-snapshot.json");
        DirtyFlagFile = Path.Combine(dataDir, "running.json");
    }

    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soundboard"));

    public string DataDir { get; }
    public string SoundsDir { get; }

    /// <summary>Copies of the pad pictures.</summary>
    public string ImagesDir { get; }

    public string ProfileFile { get; }

    /// <summary>Automatic backups of the pads (ZIP) made before an import.</summary>
    public string BackupsDir { get; }

    /// <summary>Test recordings (10-second WAV files).</summary>
    public string RecordingsDir { get; }

    /// <summary>Saved Windows audio state (see SystemState.AudioSnapshot).</summary>
    public string SnapshotFile { get; }

    /// <summary>Exists while the app runs; left over after a crash.</summary>
    public string DirtyFlagFile { get; }

    /// <summary>
    /// Copies a sound file into the sound folder (original stays) and returns the new file name.
    /// If a file with that name exists and has the same content, it is reused; otherwise a
    /// number is appended.
    /// </summary>
    public string ImportSound(string sourcePath) => Import(SoundsDir, sourcePath);

    /// <summary>
    /// Checks a picture (exists, picture ending, readable header) and copies it into the picture
    /// folder like <see cref="ImportSound"/>. Returns the file name inside the picture folder.
    /// Throws <see cref="Images.ImageImportException"/> with the reason for the user.
    /// </summary>
    public string ImportImage(string sourcePath)
    {
        Images.ImageFiles.Check(sourcePath);
        return Import(ImagesDir, sourcePath);
    }

    /// <summary>Full path of a picture stored in the profile; null for none or an unusable name.</summary>
    public string? ImagePath(string? fileName) =>
        Images.ImageFiles.IsPlainFileName(fileName) ? Path.Combine(ImagesDir, fileName!) : null;

    /// <summary>List of pictures the app let go of (removed, replaced, pad removed), waiting to be deleted.</summary>
    public string ReleasedImagesFile => Path.Combine(ImagesDir, ".loeschen");

    /// <summary>
    /// Notes that no pad needs this picture any more. It is not deleted yet ("Rückgängig" may bring
    /// it back); <see cref="RemoveReleasedImages"/> deletes it later if it is still unused.
    /// </summary>
    public void ReleaseImage(string? fileName)
    {
        if (!Images.ImageFiles.IsPlainFileName(fileName) || !Directory.Exists(ImagesDir)) return;
        try
        {
            File.AppendAllLines(ReleasedImagesFile, [fileName!]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the tidying is missing; the picture stays.
        }
    }

    /// <summary>
    /// Deletes the released pictures that no pad in <paramref name="used"/> needs. Pictures the app
    /// never released (copied there by hand, kept from an older installation) are never touched.
    /// Returns how many went.
    /// </summary>
    public int RemoveReleasedImages(IEnumerable<string?> used)
    {
        if (!File.Exists(ReleasedImagesFile)) return 0;
        var keep = new HashSet<string>(used.Where(u => u is not null)!, StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        var stillPending = new List<string>();
        try
        {
            foreach (var name in File.ReadAllLines(ReleasedImagesFile).Select(l => l.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Images.ImageFiles.IsPlainFileName(name) || keep.Contains(name)) continue;
                var path = Path.Combine(ImagesDir, name);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        removed++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    stillPending.Add(name); // in use or protected: next time
                }
            }
            if (stillPending.Count == 0) File.Delete(ReleasedImagesFile);
            else File.WriteAllLines(ReleasedImagesFile, stillPending);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return removed;
    }

    private static string Import(string dir, string sourcePath)
    {
        Directory.CreateDirectory(dir);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        var name = baseName + ext;
        for (int i = 2; ; i++)
        {
            var dest = Path.Combine(dir, name);
            if (!File.Exists(dest))
            {
                File.Copy(sourcePath, dest);
                return name;
            }
            if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase)
                || SameContent(dest, sourcePath))
            {
                return name;
            }
            name = $"{baseName} ({i}){ext}";
        }
    }

    private static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        using var sa = fa.OpenRead();
        using var sb = fb.OpenRead();
        var ba = new byte[81920];
        var bb = new byte[81920];
        while (true)
        {
            int ra = sa.ReadAtLeast(ba, ba.Length, throwOnEndOfStream: false);
            int rb = sb.ReadAtLeast(bb, bb.Length, throwOnEndOfStream: false);
            if (ra != rb) return false;
            if (ra == 0) return true;
            if (!ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, rb))) return false;
        }
    }
}
