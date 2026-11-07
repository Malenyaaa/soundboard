using System.IO.Compression;
using System.Text.Json;
using Soundboard.Core.Images;

namespace Soundboard.Core.Config;

/// <summary>A profile file could not be read; the message says why, for the user.</summary>
public sealed class ProfileArchiveException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What an archive holds, shown before anything is changed.</summary>
public sealed record ArchivePreview(string FileName, int PadCount, int MissingSounds);

/// <summary>
/// Pads taken from an archive and their own categories, all with new IDs (the pads point to the new
/// category IDs). The favourites flag comes along.
/// </summary>
public sealed record ImportedPads(List<TileConfig> Tiles, List<CategoryConfig> Categories);

/// <summary>
/// Pads as a ZIP file to take along: config.json plus the sounds and pictures they use.
/// Reading trusts nothing in the file: only "config.json", "Sounds/&lt;name&gt;" and
/// "Bilder/&lt;name&gt;" are opened, every name must be a plain file name (no folder, no ".."), and
/// nothing is unpacked anywhere but into a fresh temporary folder before it is copied in.
/// </summary>
public static class ProfileArchive
{
    public const string ConfigEntry = "config.json";
    public const string SoundsPrefix = "Sounds/";
    public const string ImagesPrefix = "Bilder/";

    /// <summary>Largest single file taken from an archive (a sound is at most 15 minutes).</summary>
    public const long MaxEntryBytes = 512L * 1024 * 1024;

    /// <summary>Limits for a whole archive, so a small packed file cannot fill the disk.</summary>
    public const long MaxTotalBytes = 8L * 1024 * 1024 * 1024;

    public const int MaxPads = 2000;

    /// <summary>Writes the pads of <paramref name="profile"/> with their files. Missing files are left out.</summary>
    public static void Export(Profile profile, AppPaths paths, string zipPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(zipPath))!;
        Directory.CreateDirectory(dir);
        var tmp = zipPath + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(ConfigEntry, CompressionLevel.Optimal);
                using (var s = entry.Open()) JsonSerializer.Serialize(s, Shareable(profile), ProfileStore.JsonOptions);
                var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in profile.Tiles)
                {
                    Add(zip, done, SoundsPrefix, paths.SoundsDir, t.FileName, CompressionLevel.NoCompression);
                    Add(zip, done, ImagesPrefix, paths.ImagesDir, t.Image, CompressionLevel.NoCompression);
                }
            }
            File.Move(tmp, zipPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>
    /// A copy without what belongs to this computer: device IDs and names and the noted Discord
    /// microphone. The file may be handed on; another computer has other devices anyway.
    /// </summary>
    public static Profile Shareable(Profile profile)
    {
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile, ProfileStore.JsonOptions), ProfileStore.JsonOptions)!;
        copy.MonitorDeviceId = null;
        copy.MonitorDeviceName = null;
        copy.MicDeviceId = null;
        copy.MicDeviceName = null;
        copy.DiscordPreviousMic = null;
        return copy;
    }

    private static void Add(ZipArchive zip, HashSet<string> done, string prefix, string dir, string? name, CompressionLevel level)
    {
        if (!ImageFiles.IsPlainFileName(name) || !done.Add(prefix + name)) return;
        var path = Path.Combine(dir, name!);
        if (File.Exists(path)) zip.CreateEntryFromFile(path, prefix + name, level);
    }

    /// <summary>Reads the pads of an archive without changing anything. Throws <see cref="ProfileArchiveException"/>.</summary>
    public static (Profile Profile, ArchivePreview Preview) Read(string zipPath)
    {
        using var zip = Open(zipPath);
        var profile = ReadProfile(zip);
        int missing = profile.Tiles.Count(t => !ImageFiles.IsPlainFileName(t.FileName) || zip.GetEntry(SoundsPrefix + t.FileName) is null);
        return (profile, new ArchivePreview(Path.GetFileName(zipPath), profile.Tiles.Count, missing));
    }

    /// <summary>
    /// Copies the sounds and pictures of the archive into the data folder (same name and content is
    /// reused, otherwise a number is added) and returns the pads, each with a new ID, pointing to
    /// the copies. Pads whose sound is missing or unsafe are left out; a picture that cannot be used
    /// is dropped from its pad.
    /// </summary>
    public static ImportedPads Import(string zipPath, AppPaths paths)
    {
        using var zip = Open(zipPath);
        var profile = ReadProfile(zip);
        long total = 0;
        foreach (var t in profile.Tiles.Take(MaxPads))
            foreach (var (prefix, name) in new[] { (SoundsPrefix, t.FileName), (ImagesPrefix, t.Image) })
                if (ImageFiles.IsPlainFileName(name) && zip.GetEntry(prefix + name) is { } e) total += e.Length;
        if (total > MaxTotalBytes) throw new ProfileArchiveException(Text.L.Get("Archive.TooBig"));
        var temp = Path.Combine(Path.GetTempPath(), "soundboard-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var result = new List<TileConfig>();
            // New IDs for the categories, so they never clash with those already here.
            var categories = profile.Categories.Select(c => (Old: c.Id, New: new CategoryConfig { Name = c.Name.Trim(), Collapsed = c.Collapsed })).ToList();
            foreach (var t in profile.Tiles.Take(MaxPads))
            {
                var sound = Extract(zip, SoundsPrefix, t.FileName, temp);
                if (sound is null) continue;
                string? image = null;
                var picture = Extract(zip, ImagesPrefix, t.Image, temp);
                if (picture is not null)
                {
                    try { image = paths.ImportImage(picture); }
                    catch (Exception ex) when (ex is ImageImportException or IOException or UnauthorizedAccessException) { image = null; }
                }
                result.Add(new TileConfig
                {
                    Name = string.IsNullOrWhiteSpace(t.Name) ? Path.GetFileNameWithoutExtension(t.FileName) : t.Name.Trim(),
                    FileName = paths.ImportSound(sound),
                    Volume = Math.Clamp(t.Volume, 0f, 1f),
                    Color = t.Color,
                    ColorChosen = t.ColorChosen,
                    Hotkey = t.Hotkey,
                    Image = image,
                    Favorite = t.Favorite,
                    Category = categories.FirstOrDefault(c => c.Old == t.Category).New?.Id,
                });
            }
            return new ImportedPads(result, categories.Select(c => c.New).ToList());
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    private static ZipArchive Open(string zipPath)
    {
        try
        {
            return ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException ex)
        {
            throw new ProfileArchiveException(Text.L.F("Archive.NotZip", Path.GetFileName(zipPath)), ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileArchiveException(Text.L.F("Archive.CannotOpen", Path.GetFileName(zipPath), ex.Message), ex);
        }
    }

    private static Profile ReadProfile(ZipArchive zip)
    {
        var entry = zip.GetEntry(ConfigEntry) ?? throw new ProfileArchiveException(Text.L.Get("Archive.NoConfig"));
        if (entry.Length > 16 * 1024 * 1024) throw new ProfileArchiveException(Text.L.Get("Archive.BadConfig"));
        try
        {
            using var s = entry.Open();
            var profile = JsonSerializer.Deserialize<Profile>(s, ProfileStore.JsonOptions) ?? throw new JsonException("empty");
            profile.Tiles ??= [];
            profile.Tiles.RemoveAll(t => t is null);
            profile.Appearance ??= AppearanceModes.System;
            profile.AccentId ??= Design.AccentPalette.DefaultId;
            profile.PadColors ??= PadColorModes.Colorful;
            profile.Language ??= Text.L.English;
            profile.Migrate();
            return profile;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidDataException or IOException)
        {
            throw new ProfileArchiveException(Text.L.Get("Archive.BadConfig"), ex);
        }
    }

    /// <summary>Unpacks one named file into <paramref name="temp"/>; null if the name is unsafe, the file is missing, too big or broken.</summary>
    private static string? Extract(ZipArchive zip, string prefix, string? name, string temp)
    {
        if (!ImageFiles.IsPlainFileName(name)) return null;
        var entry = zip.GetEntry(prefix + name);
        if (entry is null || entry.Length > MaxEntryBytes) return null;
        var folder = Path.Combine(temp, prefix.TrimEnd('/'));
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(name!));
        // Belt and braces: the target must stay inside the temporary folder.
        if (!Path.GetFullPath(target).StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            entry.ExtractToFile(target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }
}
