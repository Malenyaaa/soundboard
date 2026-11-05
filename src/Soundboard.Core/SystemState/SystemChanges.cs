using System.Security;
using Soundboard.Core.Audio;
using Soundboard.Core.Text;

namespace Soundboard.Core.SystemState;

/// <summary>Result of making one change.</summary>
public enum ApplyOutcome
{
    /// <summary>Changed now (and written to the journal before).</summary>
    Changed,

    /// <summary>Windows already had the wanted value; nothing changed, nothing journaled.</summary>
    AlreadySet,

    /// <summary>The app had already changed it (entry in the journal); kept, now owned by this run.</summary>
    AlreadyOurs,

    /// <summary>Not touched: the value is of an unknown kind, or the target is not the cable.</summary>
    Skipped,

    Failed,
}

/// <summary>Result of putting one change back.</summary>
public enum UndoOutcome
{
    /// <summary>Old value written back (or the value deleted, if it had been missing).</summary>
    Restored,

    /// <summary>Windows no longer has the app's value (never set, or someone changed it since): left as it is.</summary>
    LeftAsIs,

    /// <summary>The device is gone for good (e.g. the cable driver was removed): nothing left to put back.</summary>
    Gone,

    /// <summary>Not possible now (device disabled, Windows refused); the entry stays for the next try.</summary>
    Failed,
}

public sealed record UndoStep(ChangeEntry Entry, UndoOutcome Outcome, string? Error);

public sealed class UndoResult
{
    public required IReadOnlyList<UndoStep> Steps { get; init; }

    public int RestoredCount => Steps.Count(s => s.Outcome == UndoOutcome.Restored);

    public bool AllDone => Steps.All(s => s.Outcome != UndoOutcome.Failed);

    public IEnumerable<UndoStep> Failed => Steps.Where(s => s.Outcome == UndoOutcome.Failed);

    public static UndoResult Empty { get; } = new() { Steps = [] };
}

/// <summary>
/// Windows settings the app changes to make things easier, and puts back reliably. Every change
/// goes through the journal: the entry with the old value is written first, then Windows is
/// changed, then the entry is marked applied; putting back removes the entry. Putting back is
/// idempotent and careful: it only writes the old value while Windows still has the app's value
/// – a value someone changed since is left alone.
/// </summary>
public sealed class SystemChanges
{
    public const string DuckingKey = "ducking";

    /// <summary>Stands for the journal file itself in a result (it could not be read or written).</summary>
    public const string JournalKey = "journal";
    public const string AudioSubKey = @"Software\Microsoft\Multimedia\Audio";
    public const string DuckingValueName = "UserDuckingPreference";

    /// <summary>Windows "Communications" tab: 0 mute all other sounds, 1 reduce by 80 % (default), 2 reduce by 50 %, 3 do nothing.</summary>
    public const int DuckingDoNothing = 3;

    /// <summary>What Windows uses when the value is missing.</summary>
    public const int DuckingWindowsDefault = 1;

    private readonly ChangeJournal _journal;
    private readonly IUserRegistry _registry;

    public SystemChanges(ChangeJournal journal, IUserRegistry registry)
    {
        _journal = journal;
        _registry = registry;
    }

    public ChangeJournal Journal => _journal;

    public IReadOnlyList<ChangeEntry> List() => _journal.Read();

    // ---------------------------------------------------------------- ducking

    /// <summary>
    /// Sets the communications ducking to "do nothing", so other sounds keep their volume during
    /// calls. Journal first; an entry left by an earlier run is taken over with its old value
    /// (never the app's own 3 as "before").
    /// </summary>
    public (ApplyOutcome Outcome, string? Error) ApplyDucking(ChangeOwner owner)
    {
        try
        {
            return _journal.Update(list =>
            {
                var existing = list.FirstOrDefault(e => e.Key == DuckingKey);
                var now = _registry.ReadDword(AudioSubKey, DuckingValueName);
                if (existing is not null)
                {
                    if (now.Value == DuckingDoNothing)
                    {
                        // Ours from before: keep its old value (the 3 now is the app's, not the user's).
                        existing.Owner = owner;
                        existing.Applied = true;
                        return (ApplyOutcome.AlreadyOurs, (string?)null);
                    }
                    // Never applied (crash in between) or changed by someone since: what Windows has now is the value before.
                    list.Remove(existing);
                }
                if (now.Exists && now.Value is null) return (ApplyOutcome.Skipped, null); // not a DWORD: not ours to touch
                if (now.Value == DuckingDoNothing) return (ApplyOutcome.AlreadySet, null);

                var entry = new ChangeEntry
                {
                    Key = DuckingKey,
                    Kind = ChangeKind.Ducking,
                    Scope = ChangeScope.Session,
                    Owner = owner,
                    At = DateTime.Now,
                    OldMissing = !now.Exists,
                    OldValue = now.Value,
                    NewValue = DuckingDoNothing,
                };
                list.Add(entry);
                _journal.SaveLocked(list); // write-ahead: on disk before Windows changes
                _registry.WriteDword(AudioSubKey, DuckingValueName, DuckingDoNothing);
                entry.Applied = true;
                return (ApplyOutcome.Changed, null);
            });
        }
        catch (Exception ex) when (IsSettingError(ex))
        {
            // The entry may be on disk without the change; putting back finds the old value in
            // place and only drops the entry.
            UndoKey(DuckingKey);
            return (ApplyOutcome.Failed, ex.Message);
        }
    }

    private UndoOutcome UndoDucking(ChangeEntry e)
    {
        var now = _registry.ReadDword(AudioSubKey, DuckingValueName);
        if (now.Value != e.NewValue) return UndoOutcome.LeftAsIs; // never set, or changed by someone since
        if (e.OldMissing) _registry.DeleteValue(AudioSubKey, DuckingValueName);
        else if (e.OldValue is { } old) _registry.WriteDword(AudioSubKey, DuckingValueName, old);
        else return UndoOutcome.LeftAsIs;
        return UndoOutcome.Restored;
    }

    // ---------------------------------------------------------------- putting back

    /// <summary>Session changes of the given run (exit, watcher).</summary>
    public UndoResult UndoSession(int processId) =>
        Undo(e => e.Scope == ChangeScope.Session && e.Owner?.ProcessId == processId);

    /// <summary>
    /// Session changes whose run is over (at start: left by a crash without watcher, power loss).
    /// <paramref name="isAlive"/> says whether the owning process still runs.
    /// </summary>
    public UndoResult UndoOrphanedSession(Func<ChangeOwner, bool> isAlive) =>
        Undo(e => e.Scope == ChangeScope.Session && (e.Owner is null || !isAlive(e.Owner)));

    /// <summary>Everything (Undo all, --restore, --uninstall).</summary>
    public UndoResult UndoAll() => Undo(_ => true);

    /// <summary>One entry by key (a switch turned off).</summary>
    public UndoResult UndoKey(string key) => Undo(e => e.Key == key);

    public UndoResult Undo(Func<ChangeEntry, bool> which)
    {
        try
        {
            return _journal.Update(list =>
            {
                var steps = new List<UndoStep>();
                foreach (var e in list.Where(which).ToList())
                {
                    UndoOutcome outcome;
                    string? error = null;
                    try
                    {
                        outcome = e.Kind switch
                        {
                            ChangeKind.Ducking => UndoDucking(e),
                            _ => throw new NotSupportedException(L.F("Changes.UnknownKind", e.Kind)),
                        };
                    }
                    catch (Exception ex) when (IsSettingError(ex))
                    {
                        outcome = UndoOutcome.Failed;
                        error = ex.Message;
                    }
                    if (outcome != UndoOutcome.Failed) list.Remove(e);
                    steps.Add(new UndoStep(e, outcome, error));
                }
                return new UndoResult { Steps = steps };
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The journal itself is not readable or writable: nothing was changed back.
            return new UndoResult { Steps = [new UndoStep(new ChangeEntry { Key = JournalKey }, UndoOutcome.Failed, ex.Message)] };
        }
    }

    // ---------------------------------------------------------------- texts

    /// <summary>Short result of putting changes back, one line each (Audio state tab, removal guide, --restore).</summary>
    public static string Format(UndoResult result)
    {
        if (result.Steps.Count == 0) return L.Get("Changes.NothingToUndo");
        var lines = new List<string> { L.Count("Changes.Undone", result.RestoredCount) };
        foreach (var s in result.Steps.Where(s => s.Outcome == UndoOutcome.LeftAsIs))
            lines.Add(L.F("Changes.LeftAsIs", Describe(s.Entry)));
        foreach (var s in result.Failed)
            lines.Add(L.F("Changes.UndoFailed", Describe(s.Entry), s.Error));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>"Ducking: off, was 80 %" – one line for the list in the Audio state tab.</summary>
    public static string Describe(ChangeEntry e) => e.Key == JournalKey ? L.Get("Changes.Journal") : e.Kind switch
    {
        ChangeKind.Ducking => L.F("Changes.Ducking", DuckingText(e.NewValue), e.OldMissing ? DuckingText(null) : DuckingText(e.OldValue)),
        ChangeKind.CableFormat => L.F("Changes.CableFormat", e.DeviceName ?? "?", RateText(e.NewRate), RateText(e.OldRate)),
        _ => e.Key,
    };

    public static string DuckingText(int? value) => value switch
    {
        0 => L.Get("Changes.DuckMute"),
        1 or null => L.Get("Changes.Duck80"),
        2 => L.Get("Changes.Duck50"),
        3 => L.Get("Changes.DuckOff"),
        _ => value.Value.ToString(L.Culture),
    };

    public static string RateText(int? hz) => hz is null ? "?" : L.F("Changes.Rate", hz.Value / 1000.0);

    internal static bool IsSettingError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or System.Runtime.InteropServices.COMException
            or InvalidOperationException or NotSupportedException or ArgumentException;
}
