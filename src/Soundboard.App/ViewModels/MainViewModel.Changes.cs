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

    /// <summary>One line per change at the end of the Mic &amp; cable tab ("Other sounds during calls: …, was …").</summary>
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
        _changes = new SystemChanges(new ChangeJournal(_paths.ChangesFile), _services.Registry, _services.Formats);
        using (var me = Process.GetCurrentProcess()) _owner = new ChangeOwner(Environment.ProcessId, me.StartTime);
        _changes.UndoOrphanedSession(IsOwnerAlive);
        KeepFullVolumeInCalls = _profile.KeepFullVolumeInCalls; // _changesReady is still false: only shows the switch
        CableAt48k = _profile.CableAt48k;
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

    /// <summary>Re-reads the journal for the list at the end of the Mic &amp; cable tab.</summary>
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
        // The cable stream would break when its format changes under it: stop it first, it reopens afterwards.
        if (_mixPurpose == MixPurpose.Cable && _changes.List().Any(e => e.Kind == ChangeKind.CableFormat)) StopMix();
        var result = _changes.UndoAll();
        _changesReady = false; // the switches below only follow; the journal is already done
        KeepFullVolumeInCalls = false;
        _profile.KeepFullVolumeInCalls = false;
        CableAt48k = false;
        _profile.CableAt48k = false;
        _changesReady = true;
        _duckingError = null;
        _duckingSkipped = false;
        ScheduleSave();
        ChangesResultText = SystemChanges.Format(result);
        RefreshChanges();
        AfterCableFormatUndone(result.Steps.Any(s => s.Entry.Kind == ChangeKind.CableFormat && s.Outcome == UndoOutcome.Restored));
    }

    // ---------------------------------------------------------------- cable at 48 kHz

    /// <summary>"Set the cable to 48 kHz": only the two cable endpoints, kept until removal.</summary>
    [ObservableProperty]
    private bool _cableAt48k;

    /// <summary>The cable's rate and whether the soundboard converts; empty while unknown.</summary>
    [ObservableProperty]
    private string _cableFormatText = "";

    /// <summary>The cable (both endpoint IDs) the 48 kHz step has run for in this run: one try each.</summary>
    private string? _cableFormatTriedFor;

    private CableFormatResult? _cableFormat;
    private System.Windows.Threading.DispatcherTimer? _cableOpenTimer;

    partial void OnCableAt48kChanged(bool value)
    {
        if (!_changesReady) return;
        if (_profile.CableAt48k != value)
        {
            _profile.CableAt48k = value;
            ScheduleSave();
        }
        _cableFormatTriedFor = null;
        ChangesResultText = "";
        if (!value)
        {
            if (_mixPurpose == MixPurpose.Cable && _changes.List().Any(e => e.Kind == ChangeKind.CableFormat)) StopMix();
            bool restored = false;
            foreach (var e in _changes.List().Where(e => e.Kind == ChangeKind.CableFormat))
                restored |= _changes.UndoKey(e.Key).RestoredCount > 0;
            RefreshChanges();
            AfterCableFormatUndone(restored);
            return;
        }
        RefreshChanges();
        if (_mixPurpose != MixPurpose.FileTest) EnsureCable(); // stops the cable, sets 48 kHz, opens it again
    }

    /// <summary>
    /// Before the cable is opened: with the switch on, set both ends to 48 kHz once per run (a
    /// failure is not retried until the switch is turned on again). True when the format was
    /// just changed – then the caller opens the cable a moment later.
    /// </summary>
    private bool PrepareCableFormat()
    {
        if (!_changesReady || !Cable.IsReady) return false;
        string ids = Cable.Input!.Id + "|" + Cable.Output!.Id;
        if (CableAt48k && _cableFormatTriedFor != ids)
        {
            _cableFormatTriedFor = ids;
            _cableFormat = _changes.EnsureCableFormat(Cable);
            RefreshChanges();
            UpdateCableFormatText();
            // Windows raises no device event for a format change: read again for the device list.
            if (_cableFormat.Changed) ReadDevicesInBackground(views: true);
            return _cableFormat.Changed;
        }
        if (!CableAt48k || _cableFormat is null)
        {
            _cableFormat = _changes.ReadCableFormat(Cable);
            UpdateCableFormatText();
        }
        return false;
    }

    private void OpenCableSoon(bool withMic)
    {
        _cableOpenTimer?.Stop();
        _cableOpenTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _cableOpenTimer.Tick += (_, _) =>
        {
            _cableOpenTimer?.Stop();
            if (!_disposed && _mixPurpose == MixPurpose.None) EnsureCable(withMic);
        };
        _cableOpenTimer.Start();
    }

    /// <summary>The cable format went back to what it was: a running cable stream is no longer valid, so restart it.</summary>
    private void AfterCableFormatUndone(bool formatChanged)
    {
        _cableFormat = null;
        if (!formatChanged)
        {
            if (Cable.IsReady) _cableFormat = _changes.ReadCableFormat(Cable);
            UpdateCableFormatText();
            if (_mixPurpose == MixPurpose.None) EnsureCable(); // stopped above for nothing: open again
            return;
        }
        ReadDevicesInBackground(views: true);
        if (_mixPurpose == MixPurpose.Cable) StopMix();
        if (_mixPurpose == MixPurpose.None) OpenCableSoon(withMic: true);
        else UpdateCableFormatText();
    }

    private void UpdateCableFormatText()
    {
        var steps = _cableFormat?.Steps;
        int? rate = steps?.Select(s => s.Rate).FirstOrDefault(r => r is not null);
        if (_cableFormat is null || rate is null)
        {
            CableFormatText = "";
            return;
        }
        if (_cableFormat.AllAtRate(SystemChanges.CableRate))
        {
            CableFormatText = L.Get("Cable.Format48");
            return;
        }
        var failed = steps!.FirstOrDefault(s => s.Outcome == ApplyOutcome.Failed);
        string shown = SystemChanges.RateText(steps!.Select(s => s.Rate).FirstOrDefault(r => r is not null && r != SystemChanges.CableRate) ?? rate);
        CableFormatText = CableAt48k && failed is not null
            ? L.F("Cable.FormatFailed", shown, failed.Error)
            : L.F("Cable.FormatOther", shown);
    }

    /// <summary>Puts back this run's session changes (exit, Windows signing out). Safe to call more than once.</summary>
    public void UndoSessionChanges()
    {
        if (!_changesReady) return;
        _changes.UndoSession(_owner);
    }
}
