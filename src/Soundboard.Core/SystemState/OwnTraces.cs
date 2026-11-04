using Soundboard.Core.Config;

namespace Soundboard.Core.SystemState;

/// <summary>One item the app created, and whether removing it worked.</summary>
public sealed record TraceItem(string Description, string Path, bool Removed, string? Error);

/// <summary>
/// The app's own files in the data folder: settings, snapshots, running flag, test
/// recordings and (only on request) the sound and picture folders. Nothing outside the data folder.
/// </summary>
public static class OwnTraces
{
    /// <summary>What would be removed, for showing before the user confirms.</summary>
    public static IReadOnlyList<(string Description, string Path)> List(AppPaths paths, bool keepSounds)
    {
        var list = new List<(string, string)>();
        void AddFile(string description, string path)
        {
            if (File.Exists(path)) list.Add((description, path));
        }

        AddFile(Text.L.Get("Traces.Settings"), paths.ProfileFile);
        AddFile(Text.L.Get("Traces.SettingsTemp"), paths.ProfileFile + ".tmp");
        if (Directory.Exists(paths.DataDir))
        {
            foreach (var f in Directory.EnumerateFiles(paths.DataDir, "config.defekt-*.json")) list.Add((Text.L.Get("Traces.SettingsBroken"), f));
            foreach (var f in Directory.EnumerateFiles(paths.DataDir, "audio-snapshot*.json")) list.Add((Text.L.Get("Traces.Snapshot"), f));
        }
        AddFile(Text.L.Get("Traces.Running"), paths.DirtyFlagFile);
        if (Directory.Exists(paths.RecordingsDir)) list.Add((Text.L.Get("Traces.Recordings"), paths.RecordingsDir));
        if (Directory.Exists(paths.BackupsDir)) list.Add((Text.L.Get("Traces.Backups"), paths.BackupsDir));
        if (!keepSounds && Directory.Exists(paths.SoundsDir)) list.Add((Text.L.Get("Traces.Sounds"), paths.SoundsDir));
        if (!keepSounds && Directory.Exists(paths.ImagesDir)) list.Add((Text.L.Get("Traces.Images"), paths.ImagesDir));
        return list;
    }

    /// <summary>Removes the listed items. The data folder itself goes only if it is empty afterwards.</summary>
    public static IReadOnlyList<TraceItem> Remove(AppPaths paths, bool keepSounds)
    {
        var result = new List<TraceItem>();
        foreach (var (description, path) in List(paths, keepSounds))
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else File.Delete(path);
                result.Add(new TraceItem(description, path, true, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Add(new TraceItem(description, path, false, ex.Message));
            }
        }

        try
        {
            if (Directory.Exists(paths.DataDir) && !Directory.EnumerateFileSystemEntries(paths.DataDir).Any())
            {
                Directory.Delete(paths.DataDir);
                result.Add(new TraceItem(Text.L.Get("Traces.DataDirEmpty"), paths.DataDir, true, null));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Add(new TraceItem(Text.L.Get("Traces.DataDir"), paths.DataDir, false, ex.Message));
        }
        return result;
    }
}
