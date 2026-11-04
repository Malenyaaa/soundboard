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
    private readonly AudioSnapshot? _snapshot;
    private readonly string? _previousMic;
    private readonly DispatcherTimer _runningTimer;

    public UninstallWindow(AppPaths paths, Func<AudioStateReport> readState, IDefaultEndpointWriter writer, Func<bool> isRunning)
    {
        InitializeComponent();
        Text.UiLanguage.Attach(this);
        _paths = paths;
        _readState = readState;
        _writer = writer;
        _isRunning = isRunning;

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
        UpdateTraces();
        Compare();
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
        var autostart = Autostart.Read();
        var sb = new StringBuilder();
        if (items.Count == 0 && autostart is null)
        {
            sb.Append(L.Get("Uninstall.NoData"));
        }
        else
        {
            sb.Append(L.F("Uninstall.WillDelete", _paths.DataDir));
            foreach (var group in items.GroupBy(i => i.Description)) sb.Append(Environment.NewLine).Append("•  ").Append(group.Key);
            if (autostart is not null) sb.Append(Environment.NewLine).Append("•  ").Append(L.Get("Uninstall.AutostartEntry"));
            if (keep && (Directory.Exists(_paths.SoundsDir) || Directory.Exists(_paths.ImagesDir))) sb.Append(Environment.NewLine).Append(L.Get("Uninstall.FoldersStay"));
        }
        TracesText.Text = sb.ToString();
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
        if (MessageBox.Show(this, question, L.Get("Uninstall.AskTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var removed = OwnTraces.Remove(_paths, keep);
        var sb = new StringBuilder();
        int ok = removed.Count(r => r.Removed);
        sb.Append(L.Count("Uninstall.Deleted", ok));
        if (Autostart.Read() is not null)
        {
            var err = Autostart.Remove();
            sb.Append(' ').Append(err is null ? L.Get("Uninstall.AutostartRemoved") : L.F("Uninstall.AutostartFailed", err));
        }
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
