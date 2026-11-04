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

    /// <summary>Global hotkey as token text (e.g. "NumPad1"); null = none.</summary>
    public string? Hotkey { get; set; }
}

/// <summary>User settings: tiles and the chosen monitor device. Stored as JSON.</summary>
public sealed class Profile
{
    public int Version { get; set; } = 1;

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

    /// <summary>Global hotkey for "stop all" as token text; null = none.</summary>
    public string? StopAllHotkey { get; set; } = "Pause";

    public List<TileConfig> Tiles { get; set; } = [];
}

/// <summary>Result of loading a profile; <see cref="Problem"/> is a German hint if something went wrong.</summary>
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
                ? "Die Einstellungen ließen sich nicht lesen und wurden zurückgesetzt."
                : $"Die Einstellungen ließen sich nicht lesen und wurden zurückgesetzt. Die alte Datei liegt als „{Path.GetFileName(backup)}“ im Datenordner.";
            return new ProfileLoadResult(new Profile(), hint);
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
        ProfileFile = Path.Combine(dataDir, "config.json");
        RecordingsDir = Path.Combine(dataDir, "Aufnahmen");
        SnapshotFile = Path.Combine(dataDir, "audio-snapshot.json");
        DirtyFlagFile = Path.Combine(dataDir, "running.json");
    }

    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soundboard"));

    public string DataDir { get; }
    public string SoundsDir { get; }
    public string ProfileFile { get; }

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
    public string ImportSound(string sourcePath)
    {
        Directory.CreateDirectory(SoundsDir);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        var name = baseName + ext;
        for (int i = 2; ; i++)
        {
            var dest = Path.Combine(SoundsDir, name);
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
