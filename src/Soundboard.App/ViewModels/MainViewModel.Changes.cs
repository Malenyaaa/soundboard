using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.SystemState;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>
/// Windows settings the app changes to make things easier – and puts back: at exit, by the
/// watcher after a crash, at the next start after a crash without watcher, and on "Undo all
/// changes". Every change is in the journal (changes.json) with the value before.
/// </summary>
public sealed partial class MainViewModel
{
    private SystemChanges _changes = null!;
    private ChangeOwner _owner = null!;
    private bool _changesReady;

    /// <summary>One line per change in the Audio state tab ("Other sounds during calls: …, was …").</summary>
    public ObservableCollection<string> ChangeLines { get; } = [];

    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>What the last "Undo all changes" did; empty otherwise.</summary>
    [ObservableProperty]
    private string _changesResultText = "";

    /// <summary>"Keep other sounds at full volume during calls" (Windows communications ducking off while the app runs).</summary>
    [ObservableProperty]
    private bool _keepFullVolumeInCalls;

    /// <summary>Why the ducking setting could not be changed; empty when it worked.</summary>
    [ObservableProperty]
    private string _duckingStatusText = "";

    private string? _duckingError;
    private bool _duckingSkipped;

    /// <summary>
    /// At start: first put back what an earlier run left (crash without watcher, power loss), then
    /// make this run's changes.
    /// </summary>
    private void InitChanges()
    {
        _changes = new SystemChanges(new ChangeJournal(_paths.ChangesFile), _services.Registry);
        using (var me = Process.GetCurrentProcess()) _owner = new ChangeOwner(Environment.ProcessId, me.StartTime);
        _changes.UndoOrphanedSession(IsOwnerAlive);
        KeepFullVolumeInCalls = _profile.KeepFullVolumeInCalls; // _changesReady is still false: only shows the switch
        _changesReady = true;
        if (KeepFullVolumeInCalls) ApplyDucking();
        RefreshChanges();
    }

    private static bool IsOwnerAlive(ChangeOwner o) =>
        !DirtyFlag.IsStale(new DirtyInfo(o.ProcessId, o.StartedAt, false), DirtyFlag.ProcessStartTime);

    partial void OnKeepFullVolumeInCallsChanged(bool value)
    {
        if (!_changesReady) return;
        if (_profile.KeepFullVolumeInCalls != value)
        {
            _profile.KeepFullVolumeInCalls = value;
            ScheduleSave();
        }
        if (value) ApplyDucking();
        else
        {
            _changes.UndoKey(SystemChanges.DuckingKey);
            _duckingError = null;
            _duckingSkipped = false;
        }
        ChangesResultText = "";
        RefreshChanges();
    }

    private void ApplyDucking()
    {
        var (outcome, error) = _changes.ApplyDucking(_owner);
        _duckingError = outcome == ApplyOutcome.Failed ? error : null;
        _duckingSkipped = outcome == ApplyOutcome.Skipped;
    }

    /// <summary>Re-reads the journal for the list in the Audio state tab.</summary>
    private void RefreshChanges()
    {
        if (!_changesReady) return;
        ChangeLines.Clear();
        foreach (var e in _changes.List().OrderBy(e => e.Kind)) ChangeLines.Add(SystemChanges.Describe(e));
        HasChanges = ChangeLines.Count > 0;
        DuckingStatusText = !KeepFullVolumeInCalls ? ""
            : _duckingError is not null ? L.F("Changes.DuckingFailed", _duckingError)
            : _duckingSkipped ? L.Get("Changes.DuckingSkipped")
            : "";
    }

    /// <summary>
    /// "Undo all changes": every Windows setting the app changed goes back now, and the switches
    /// that made them are turned off, so nothing comes back by itself.
    /// </summary>
    [RelayCommand]
    private void UndoAllChanges()
    {
        var result = _changes.UndoAll();
        _changesReady = false; // the switches below only follow; the journal is already done
        KeepFullVolumeInCalls = false;
        _profile.KeepFullVolumeInCalls = false;
        _changesReady = true;
        _duckingError = null;
        _duckingSkipped = false;
        ScheduleSave();
        ChangesResultText = SystemChanges.Format(result);
        RefreshChanges();
    }

    /// <summary>Puts back this run's session changes (exit, Windows signing out). Safe to call more than once.</summary>
    public void UndoSessionChanges()
    {
        if (!_changesReady) return;
        _changes.UndoSession(Environment.ProcessId);
    }
}
