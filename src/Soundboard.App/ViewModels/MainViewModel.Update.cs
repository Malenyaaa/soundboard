using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Text;
using Soundboard.Core.Web;

namespace Soundboard.App.ViewModels;

/// <summary>
/// Automatic updates (switch in the appearance tab, on by default): shortly after the start and
/// then every six hours the app asks GitHub for a newer stable release, fetches its setup quietly
/// and checks it. Then the status line says so in small print; a click there installs at once and
/// starts again, otherwise the update installs when the app is quit. Only for an installed copy
/// (a build from the source tree has no uninstaller next to it and is never replaced).
/// </summary>
public sealed partial class MainViewModel
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private AppUpdater? _updater;
    private DispatcherTimer? _updateTimer;
    private readonly CancellationTokenSource _updateStop = new();
    private bool _updateRunning;
    private Version? _readyVersion;

    /// <summary>Setup of a checked newer version, waiting for the app to end; null if there is none.</summary>
    public string? PendingSetup { get; private set; }

    /// <summary>The user clicked the update in the status line: the app installs it now and starts again.</summary>
    public event EventHandler? UpdateRestartRequested;

    /// <summary>Version of this copy, e.g. "1.0.0".</summary>
    public static string AppVersionText => CurrentVersion.ToString(3);

    private static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>"Update 1.0.1 ready – restart" in the status line; empty while there is none.</summary>
    [ObservableProperty]
    private string _updateReadyText = "";

    [ObservableProperty]
    private string _updateReadyTip = "";

    /// <summary>Version line under the switch: this copy, and a fetched update if any.</summary>
    [ObservableProperty]
    private string _updateStatusText = "";

    public bool AutoUpdate
    {
        get => _profile.AutoUpdate;
        set
        {
            if (value == _profile.AutoUpdate) return;
            _profile.AutoUpdate = value;
            ScheduleSave();
            OnPropertyChanged();
            if (value) _ = CheckForUpdateAsync();
            UpdateUpdateTexts();
        }
    }

    /// <summary>True when this copy was installed by the setup (an uninstaller lies next to it).</summary>
    public static bool IsInstalledCopy =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    /// <summary>Called once by the app after the window exists (not for previews and tests).</summary>
    public void StartUpdates()
    {
        UpdateUpdateTexts();
        if (!IsInstalledCopy) return;
        _updater = new AppUpdater(_paths.UpdatesDir, CurrentVersion);
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = FirstCheck };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = CheckEvery;
            _ = CheckForUpdateAsync();
        };
        _updateTimer.Start();
    }

    private async Task CheckForUpdateAsync()
    {
        if (_updater is null || _disposed || _updateRunning || !_profile.AutoUpdate) return;
        _updateRunning = true;
        try
        {
            var release = await _updater.CheckAsync(_updateStop.Token);
            if (release is null || _disposed) return;
            if (_readyVersion is not null && _readyVersion >= release.Version) return;
            var setup = await _updater.DownloadAsync(release, _updateStop.Token);
            if (_disposed) return;
            PendingSetup = setup;
            _readyVersion = release.Version;
            UpdateUpdateTexts();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or InvalidDataException or OperationCanceledException)
        {
            // Offline, rate limit, damaged download or the app closes: the next check tries again.
        }
        finally
        {
            _updateRunning = false;
        }
    }

    private void UpdateUpdateTexts()
    {
        bool ready = _readyVersion is not null && PendingSetup is not null;
        string next = _readyVersion?.ToString(3) ?? "";
        UpdateReadyText = ready ? L.F("Update.Ready", next) : "";
        UpdateReadyTip = ready ? L.F(_profile.AutoUpdate ? "Update.ReadyTip" : "Update.ReadyTipOff", next) : "";
        UpdateStatusText = !IsInstalledCopy ? L.F("Update.NotInstalled", AppVersionText)
            : ready ? L.F("Update.StatusReady", AppVersionText, next)
            : L.F("Update.Status", AppVersionText);
    }

    /// <summary>Click on the update in the status line.</summary>
    [RelayCommand]
    private void InstallUpdateNow()
    {
        if (PendingSetup is null || !File.Exists(PendingSetup)) return;
        UpdateRestartRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>At exit: the setup to run (null if none, or automatic updates are off and the user did not click).</summary>
    public string? SetupToRunAtExit(bool clicked) =>
        PendingSetup is { } s && File.Exists(s) && (clicked || _profile.AutoUpdate) ? s : null;

    private void StopUpdates()
    {
        _updateTimer?.Stop();
        _updateStop.Cancel();
    }
}
