using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Soundboard.App.ViewModels;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;
using Soundboard.Core.SystemState;
using Soundboard.Core.Text;

namespace Soundboard.App.Views;

/// <summary>
/// Guided removal (<c>Soundboard.exe --uninstall</c>, also from the tray menu): check the
/// audio state and offer "Restore default device", delete the app's own data after a question (sounds
/// kept unless unticked), explain how to remove the cable driver, and compare at the end.
/// Snapshot and the noted Discord microphone are read once at the start, so they are still
/// there for the last step after the data has been deleted.
/// </summary>
public partial class UninstallWindow : Window
{

    private readonly AppPaths _paths;
    private readonly Func<AudioStateReport> _readState;
    private readonly IDefaultEndpointWriter _writer;
    private readonly Func<bool> _isRunning;
    private readonly Autostart _autostart;
    private readonly AudioSnapshot? _snapshot;
    private readonly string? _previousMic;
    private readonly DispatcherTimer _runningTimer;

    public UninstallWindow(AppPaths paths, Func<AudioStateReport> readState, IDefaultEndpointWriter writer, Func<bool> isRunning, Autostart autostart, SystemChanges changes)
    {
        InitializeComponent();
        Text.UiLanguage.Attach(this);
        _paths = paths;
        _readState = readState;
        _writer = writer;
        _isRunning = isRunning;
        _autostart = autostart;

        try { _snapshot = SnapshotStore.Load(paths.SnapshotFile); }
        catch (SnapshotException) { _snapshot = null; }
        _previousMic = ProfileStore.Load(paths.ProfileFile).Profile.DiscordPreviousMic;

        DiscordText.Text = string.IsNullOrWhiteSpace(_previousMic)
            ? L.F("Uninstall.Discord", MainViewModel.DiscordWayBack(null))
            : L.F("Uninstall.DiscordNoted", _previousMic, MainViewModel.DiscordWayBack(_previousMic));

        _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runningTimer.Tick += (_, _) => UpdateRunning();
        _runningTimer.Start();
        Closed += (_, _) => _runningTimer.Stop();

        UpdateRunning();
        UndoChanges(changes);
        UpdateTraces();
        Compare();
    }

    /// <summary>
    /// Removing the soundboard puts back every Windows setting it changed (journal), at once and
    /// without a button: that is what removing means. Step 1 says what happened.
    /// </summary>
    private void UndoChanges(SystemChanges changes)
    {
        if (changes.List().Count == 0) return;
        ChangesText.Text = L.Get("Uninstall.ChangesUndone") + Environment.NewLine + SystemChanges.Format(changes.UndoAll());
        ChangesText.Visibility = Visibility.Visible;
    }

    private void UpdateRunning() =>
        RunningBox.Visibility = _isRunning() ? Visibility.Visible : Visibility.Collapsed;

    private void Compare()
    {
        var now = _readState();
        if (_snapshot is null)
        {
            StateText.Text = L.Get("Uninstall.NoSnapshot");
            RestoreButton.Visibility = Visibility.Collapsed;
            Table.ItemsSource = null;
            CompareText.Text = "";
            return;
        }

        var cmp = SnapshotService.Compare(_snapshot, now);
        string when = L.DateTime(_snapshot.TakenAt);
        StateText.Text = cmp.DefaultsChanged
            ? L.F("Cable.DefaultsChanged", when, string.Join("; ", cmp.DefaultChangeLines))
            : L.F("Uninstall.Same", when);
        RestoreButton.Visibility = cmp.DefaultsChanged ? Visibility.Visible : Visibility.Collapsed;
        Table.ItemsSource = cmp.Rows;
        CompareText.Text = cmp.IsIdentical
            ? L.Get("Uninstall.AllSame")
            : L.Count("Uninstall.Diff", cmp.DifferenceCount);
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null) return;
        var result = SnapshotService.Restore(_snapshot, _readState(), _writer);
        var sb = new StringBuilder(L.Count("Restore.Short", result.RestoredCount));
        if (!result.AllGood) sb.Append(' ').Append(L.Get("Restore.ByHandShort")).Append(Environment.NewLine).Append(result.ManualSteps());
        RestoreText.Text = sb.ToString();
        RestoreText.Visibility = Visibility.Visible;
        Compare();
    }

    private void OnKeepChanged(object sender, RoutedEventArgs e) => UpdateTraces();

    private void UpdateTraces()
    {
        if (TracesText is null || _paths is null) return; // during InitializeComponent
        bool keep = KeepSounds.IsChecked == true;
        var items = OwnTraces.List(_paths, keep);
        // Only an entry that starts this program file is ours; one for another copy stays.
        bool autostart = _autostart.Read().Kind == AutostartKind.ThisCopy;
        var sb = new StringBuilder();
        bool nothing = items.Count == 0 && !autostart;
        // The folder stands on its own line (never broken inside), the list below it.
        TracesHead.Text = L.Get(nothing ? "Uninstall.NoData" : "Uninstall.WillDelete");
        DataPathBox.Text = _paths.DataDir;
        DataPathBox.Visibility = nothing ? Visibility.Collapsed : Visibility.Visible;
        if (!nothing)
        {
            foreach (var group in items.GroupBy(i => i.Description)) sb.Append(sb.Length == 0 ? "" : Environment.NewLine).Append("•  ").Append(group.Key);
            if (autostart) sb.Append(sb.Length == 0 ? "" : Environment.NewLine).Append("•  ").Append(L.Get("Uninstall.AutostartEntry"));
            if (keep && (Directory.Exists(_paths.SoundsDir) || Directory.Exists(_paths.ImagesDir))) sb.Append(Environment.NewLine).Append(L.Get("Uninstall.FoldersStay"));
        }
        TracesText.Text = sb.ToString();
        TracesText.Visibility = nothing ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRemoveTraces(object sender, RoutedEventArgs e)
    {
        if (_isRunning())
        {
            RemoveText.Text = L.Get("Uninstall.StillRunning");
            RemoveText.Visibility = Visibility.Visible;
            return;
        }
        bool keep = KeepSounds.IsChecked == true;
        var question = L.Get(keep ? "Uninstall.AskKeep" : "Uninstall.AskAll");
        if (!ConfirmWindow.Ask(this, L.Get("Uninstall.AskTitle"), L.Get("Uninstall.AskHeading"), question, L.Get("Uninstall.Delete"), L.Get("Common.Cancel")))
            return;

        var removed = OwnTraces.Remove(_paths, keep);
        var sb = new StringBuilder();
        int ok = removed.Count(r => r.Removed);
        sb.Append(L.Count("Uninstall.Deleted", ok));
        if (_autostart.RemoveIfThisCopy(out var err))
            sb.Append(' ').Append(err is null ? L.Get("Uninstall.AutostartRemoved") : L.F("Uninstall.AutostartFailed", err));
        foreach (var f in removed.Where(r => !r.Removed))
            sb.Append(Environment.NewLine).Append(L.F("Uninstall.NotDeleted", f.Path, f.Error));
        RemoveText.Text = sb.ToString();
        RemoveText.Visibility = Visibility.Visible;
        UpdateTraces();
    }

    private void OnDeviceManager(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private void OnCompare(object sender, RoutedEventArgs e) => Compare();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
