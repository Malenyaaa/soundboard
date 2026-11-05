using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundboard.Core.SystemState;

/// <summary>What kind of Windows setting a journal entry is about.</summary>
public enum ChangeKind
{
    /// <summary>HKCU\Software\Microsoft\Multimedia\Audio, UserDuckingPreference.</summary>
    Ducking,

    /// <summary>Default format (sample rate) of a virtual cable endpoint.</summary>
    CableFormat,
}

/// <summary>When a change is put back.</summary>
public enum ChangeScope
{
    /// <summary>Only while the app runs: back at exit, by the watcher after a crash, or at the next start.</summary>
    Session,

    /// <summary>Stays while the app is installed: back only on "Undo all changes", --restore and --uninstall.</summary>
    Install,
}

/// <summary>The app process that made a session change (process ID and start time, so a reused ID is not mistaken for it).</summary>
public sealed record ChangeOwner(int ProcessId, DateTime StartedAt);

/// <summary>
/// One change the app made to Windows, with the value before it. Written before the change
/// (write-ahead, <see cref="Applied"/> false), marked applied after it, removed once it is put back.
/// </summary>
public sealed class ChangeEntry
{
    /// <summary>Unique per setting: "ducking", "cable-format:&lt;endpoint ID&gt;".</summary>
    public string Key { get; set; } = "";

    public ChangeKind Kind { get; set; }

    public ChangeScope Scope { get; set; }

    /// <summary>The process that made a session change; null for install changes.</summary>
    public ChangeOwner? Owner { get; set; }

    public DateTime At { get; set; }

    /// <summary>False between writing the entry and the change succeeding (a crash there leaves it false).</summary>
    public bool Applied { get; set; }

    // ---- ducking

    /// <summary>The registry value did not exist before (then putting back means deleting it).</summary>
    public bool OldMissing { get; set; }

    /// <summary>The registry value before (when it existed).</summary>
    public int? OldValue { get; set; }

    /// <summary>The value the app set.</summary>
    public int? NewValue { get; set; }

    // ---- cable format

    public string? DeviceId { get; set; }

    public string? DeviceName { get; set; }

    /// <summary>Direction of the cable endpoint (playback "CABLE Input", recording "CABLE Output").</summary>
    public Audio.DeviceFlow? Flow { get; set; }

    /// <summary>The endpoint's device format before (WAVEFORMATEX bytes, base64).</summary>
    public string? OldDeviceFormat { get; set; }

    /// <summary>The endpoint's mix format before (WAVEFORMATEX bytes, base64).</summary>
    public string? OldMixFormat { get; set; }

    public int? OldRate { get; set; }

    public int? NewRate { get; set; }
}

/// <summary>
/// The change journal (<c>changes.json</c> in the data folder): every Windows setting the app
/// changed, with the value before. Each read-modify-write runs under a named mutex, so the app,
/// its watcher, --restore and the removal guide never overwrite each other's entries. Writes are
/// atomic (temporary file, then move). An unreadable file is set aside, never overwritten.
/// </summary>
public sealed class ChangeJournal
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _mutexName;

    /// <summary>How long to wait for another process holding the journal; then the operation fails (and changes nothing).</summary>
    internal TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal string MutexName => _mutexName;

    public ChangeJournal(string path)
    {
        Path = path;
        // One mutex per journal file (tests use their own folders).
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(path).ToUpperInvariant()));
        _mutexName = @"Local\Soundboard.Changes." + Convert.ToHexString(hash, 0, 8);
    }

    public string Path { get; }

    /// <summary>The file could not be read last time; it was set aside as "changes.defekt-….json".</summary>
    public string? SetAsideFile { get; private set; }

    /// <summary>All entries (empty without a file).</summary>
    public IReadOnlyList<ChangeEntry> Read() => Update(list => list.ToList(), save: false);

    /// <summary>
    /// Runs <paramref name="action"/> on the entries under the lock and saves the list afterwards
    /// (if <paramref name="save"/>). The file is deleted when the list becomes empty.
    /// </summary>
    public T Update<T>(Func<List<ChangeEntry>, T> action, bool save = true)
    {
        using var mutex = new Mutex(false, _mutexName);
        bool owned = false;
        try
        {
            try { owned = mutex.WaitOne(LockTimeout); }
            catch (AbandonedMutexException) { owned = true; } // the other process died holding it; the file is still consistent (atomic writes)
            // Never read-modify-write without the lock: another process could lose an entry (and its old value).
            if (!owned) throw new IOException(Text.L.Get("Changes.JournalBusy"));
            var list = Load();
            var result = action(list);
            if (save) Save(list);
            return result;
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }

    public void Put(ChangeEntry entry) => Update(list =>
    {
        list.RemoveAll(e => e.Key == entry.Key);
        list.Add(entry);
        return 0;
    });

    public void Remove(string key) => Update(list => list.RemoveAll(e => e.Key == key));

    public ChangeEntry? Find(string key) => Read().FirstOrDefault(e => e.Key == key);

    /// <summary>Saves inside <see cref="Update{T}"/> (lock held): the write-ahead step before Windows is changed.</summary>
    internal void SaveLocked(List<ChangeEntry> list) => Save(list);

    private List<ChangeEntry> Load()
    {
        if (!File.Exists(Path)) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<ChangeEntry>>(File.ReadAllText(Path), Options) ?? [];
            list.RemoveAll(e => e is null || string.IsNullOrEmpty(e.Key));
            return list;
        }
        catch (JsonException)
        {
            // Keep the broken file for a look by hand (the removal guide does not delete it); the old
            // values in it are not lost, but they are no longer put back by themselves.
            var aside = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,
                $"changes.defekt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(Path, aside, overwrite: true);
            SetAsideFile = aside;
            return [];
        }
    }

    private void Save(List<ChangeEntry> list)
    {
        if (list.Count == 0)
        {
            if (File.Exists(Path)) File.Delete(Path);
            return;
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        // Written through to the disk before the move: after a power loss the journal is the old or the new one, not empty.
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(list, Options));
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, Path, overwrite: true);
    }
}
