using System.Text.Json;
using System.Text.Json.Serialization;
using Soundboard.Core.Audio;

namespace Soundboard.Core.SystemState;

/// <summary>A saved default device for one direction and role.</summary>
public sealed record SnapshotDefault(DeviceFlow Flow, DeviceRoleKind Role, string? Id, string? Name);

/// <summary>A saved endpoint with what was readable about it.</summary>
public sealed record SnapshotDevice(string Id, string Name, DeviceFlow Flow, DeviceStatus Status, float? Volume, bool? Muted);

/// <summary>
/// The Windows audio state at one moment, saved as audio-snapshot.json: default devices per
/// role, all endpoints with state, volume and mute. Used to detect changes made by others
/// (e.g. a driver installer) and to put the default devices back on request.
/// </summary>
public sealed class AudioSnapshot
{
    public int Version { get; set; } = 1;
    public DateTime TakenAt { get; set; }
    public string AppVersion { get; set; } = "";
    public List<SnapshotDefault> Defaults { get; set; } = [];
    public List<SnapshotDevice> Devices { get; set; } = [];

    public static AudioSnapshot From(AudioStateReport report, string appVersion) => new()
    {
        TakenAt = report.TakenAt,
        AppVersion = appVersion,
        Defaults = [.. report.Defaults.Select(d => new SnapshotDefault(d.Flow, d.Role, d.Id, d.Name))],
        Devices = [.. report.Devices.Select(d => new SnapshotDevice(d.Id, d.Name, d.Flow, d.Status, d.VolumeScalar, d.Muted))],
    };

    public SnapshotDefault? GetDefault(DeviceFlow flow, DeviceRoleKind role) =>
        Defaults.FirstOrDefault(d => d.Flow == flow && d.Role == role);
}

/// <summary>Reads and writes snapshots. Writing keeps the previous file as a backup.</summary>
public static class SnapshotStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(AudioSnapshot s) => JsonSerializer.Serialize(s, Options);

    public static AudioSnapshot? Deserialize(string json)
    {
        var s = JsonSerializer.Deserialize<AudioSnapshot>(json, Options);
        if (s is null) return null;
        s.Defaults ??= [];
        s.Devices ??= [];
        return s;
    }

    /// <summary>Null if there is no file. Throws <see cref="SnapshotException"/> if it is unreadable.</summary>
    public static AudioSnapshot? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return Deserialize(File.ReadAllText(path))
                ?? throw new SnapshotException(Text.L.Get("Snapshot.Empty"));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new SnapshotException(Text.L.F("Snapshot.Unreadable", ex.Message), ex);
        }
    }

    /// <summary>Writes atomically; an existing snapshot is kept as "…vorher.json".</summary>
    public static void Save(string path, AudioSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var backup = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".vorher.json");
            File.Copy(path, backup, overwrite: true);
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(snapshot));
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class SnapshotException(string message, Exception? inner = null) : Exception(message, inner);
