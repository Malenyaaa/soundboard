using System.Diagnostics;
using System.Text.Json;

namespace Soundboard.Core.SystemState;

/// <summary>Content of the running flag.</summary>
public sealed record DirtyInfo(int ProcessId, DateTime StartedAt, bool CableActive);

/// <summary>
/// "running.json" exists while the app runs and is deleted on a normal exit. Found at start
/// (or by the watcher) while its process is gone, it means the app did not end normally.
/// </summary>
public sealed class DirtyFlag
{
    public DirtyFlag(string path) => Path = path;

    public string Path { get; }

    public DirtyInfo? Read()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            return JsonSerializer.Deserialize<DirtyInfo>(File.ReadAllText(Path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Unreadable flag: treat as "did not end normally" with unknown details.
            return new DirtyInfo(0, DateTime.MinValue, false);
        }
    }

    public void Write(DirtyInfo info)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(info));
        File.Move(tmp, Path, overwrite: true);
    }

    public void Delete()
    {
        try { File.Delete(Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>True if the flag belongs to a process that no longer runs.</summary>
    public static bool IsStale(DirtyInfo info, Func<int, DateTime?> processStart)
    {
        if (info.ProcessId == 0) return true;
        var started = processStart(info.ProcessId);
        if (started is null) return true;
        // Same PID reused by another program: start times differ.
        return Math.Abs((started.Value - info.StartedAt).TotalSeconds) > 2;
    }

    /// <summary>Start time of a running process, or null.</summary>
    public static DateTime? ProcessStartTime(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited ? null : p.StartTime;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
