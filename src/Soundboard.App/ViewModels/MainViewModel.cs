using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Soundboard.Core.Audio;
using Soundboard.Core.Audio.Sinks;
using Soundboard.Core.Config;
using Soundboard.Core.Hotkeys;
using Soundboard.Core.SystemState;
using Soundboard.App.Themes;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>
/// Main window logic: device list (read only), tiles, monitor output. Windows audio settings
/// are only read, never changed; the monitor device is opened by ID in shared mode.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppPaths _paths;
    private readonly Profile _profile;
    private readonly IAudioSystem _audio;
    private readonly HotkeyService _hotkeys;
    private readonly AppServices _services;
    private readonly DirtyFlag _dirty;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _deviceTimer;
    private readonly SoundBank _bank;
    private PlayerGroup _player;
    private IAudioOutput? _sink;
    private AudioStateReport _state;
    private bool _syncingSelection;
    private bool _disposed;
    private bool _initialized;

    // Released pictures are only tidied up when the profile was read cleanly, and at exit only
    // when the profile on disk is current.
    private readonly bool _imagesTidyAllowed;
    private bool _lastSaveOk = true;

    public MainViewModel(AppServices services)
    {
        _services = services;
        _paths = services.Paths;
        _audio = services.Audio;
        _dirty = new DirtyFlag(_paths.DirtyFlagFile);
        _dispatcher = Dispatcher.CurrentDispatcher;

        var loaded = ProfileStore.Load(_paths.ProfileFile);
        _profile = loaded.Profile;
        // The interface speaks the language of the profile from the first text on.
        L.SetLanguage(_profile.Language);
        L.Changed += OnLanguageChanged;
        if (loaded.Problem is not null)
        {
            var problem = loaded.Problem;
            ShowNotice("profile", () => problem);
        }
        _imagesTidyAllowed = loaded.Problem is null;
        RemoveReleasedImages();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => SaveNow();
        _progressTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _deviceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _deviceTimer.Tick += (_, _) => { _deviceTimer.Stop(); OnDevicesChanged(); };

        _state = _audio.ReadState();
        _audio.DevicesChanged += (_, _) => _dispatcher.BeginInvoke(() => { _deviceTimer.Stop(); _deviceTimer.Start(); });
        _audio.StartWatching();

        // Monitor device: chosen by ID. First start: the current default playback device,
        // only read from Windows and remembered by its ID.
        if (_profile.MonitorDeviceId is null)
        {
            var def = _state.GetDefault(DeviceFlow.Render, DeviceRoleKind.Multimedia);
            if (def?.Id is not null && !CableDetector.IsCableName(def.Name))
            {
                _profile.MonitorDeviceId = def.Id;
                _profile.MonitorDeviceName = def.Name;
                ScheduleSave();
            }
        }

        var format = ReadMonitorFormat() ?? TargetFormat.Fallback;
        _bank = new SoundBank(format);
        _player = CreatePlayer(format);

        MonitorEnabled = _profile.MonitorEnabled;
        MasterVolumePercent = Math.Round(Math.Clamp(_profile.MasterVolume, 0f, 1f) * 100);
        OverlayEnabled = _profile.Overlap == OverlapMode.Overlay;

        InitMixSettings();
        RefreshDeviceViews();
        Tiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoTiles));
        foreach (var t in _profile.Tiles)
        {
            var tile = new TileViewModel(this, t);
            Tiles.Add(tile);
            _ = LoadTileAsync(tile);
            _ = LoadTileImageAsync(tile);
        }

        InitFunctionKeys();
        _hotkeys = new HotkeyService(services.Hotkeys);
        services.Hotkeys.Pressed += id => _hotkeys.HandleHotkey(id);
        _hotkeys.Triggered += (_, owner) => OnHotkey(owner);
        ApplyAllHotkeys();

        InitDesign();
        RefreshAutostart();
        ThemeManager.Changed += OnThemeChanged;
        Notices.CollectionChanged += (_, _) => UpdateNoticeView();

        InitAudioState();

        _initialized = true;
        EnsureMonitor();
        CableEnabled = _profile.CableEnabled;
        EnsureCable();
        if (!_profile.SetupSeen) OpenSettings(SettingsTabs.Setup);
        _progressTimer.Start();
    }

    public ObservableCollection<TileViewModel> Tiles { get; } = [];
    public ObservableCollection<NoticeViewModel> Notices { get; } = [];
    public ObservableCollection<DeviceRowViewModel> Devices { get; } = [];
    public ObservableCollection<OutputChoice> OutputChoices { get; } = [];

    public bool HasNoTiles => Tiles.Count == 0;

    public string SoundsDir => _paths.SoundsDir;

    [ObservableProperty]
    private int _notPresentCount;

    [ObservableProperty]
    private OutputChoice? _selectedOutput;

    [ObservableProperty]
    private bool _monitorEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterVolumeText))]
    private double _masterVolumePercent;

    [ObservableProperty]
    private bool _overlayEnabled;

    [ObservableProperty]
    private bool _showNotPresent;

    [ObservableProperty]
    private string _devicesHeader = "";

    [ObservableProperty]
    private string _statusMonitor = "";

    // ---------------------------------------------------------------- notices

    /// <summary>
    /// Shows a notice; a notice with the same key is replaced. Only one notice is visible
    /// at a time (the newest); the others wait behind "Next". The texts are produced on demand,
    /// so a notice follows a change of the interface language.
    /// </summary>
    public void ShowNotice(string key, Func<string> text, Func<string>? actionText = null, Action? action = null)
    {
        var existing = Notices.FirstOrDefault(n => n.Key == key);
        var notice = new NoticeViewModel(key, text, n => Notices.Remove(n), actionText, action);
        if (existing is not null) Notices[Notices.IndexOf(existing)] = notice;
        else Notices.Add(notice);
        CurrentNotice = notice;
        UpdateNoticeView();
    }

    /// <summary>The one notice on screen.</summary>
    [ObservableProperty]
    private NoticeViewModel? _currentNotice;

    /// <summary>"1 of 3" when more notices wait; empty otherwise.</summary>
    [ObservableProperty]
    private string _noticeCountText = "";

    private void UpdateNoticeView()
    {
        if (CurrentNotice is null || !Notices.Contains(CurrentNotice)) CurrentNotice = Notices.LastOrDefault();
        NoticeCountText = Notices.Count > 1 && CurrentNotice is not null
            ? L.F("Notice.Count", Notices.IndexOf(CurrentNotice) + 1, Notices.Count)
            : "";
    }

    [RelayCommand]
    private void NextNotice()
    {
        if (Notices.Count == 0) return;
        int i = CurrentNotice is null ? -1 : Notices.IndexOf(CurrentNotice);
        CurrentNotice = Notices[(i + 1) % Notices.Count];
        UpdateNoticeView();
    }

    public void ClearNotice(string key)
    {
        var existing = Notices.FirstOrDefault(n => n.Key == key);
        if (existing is not null) Notices.Remove(existing);
    }

    // ---------------------------------------------------------------- devices

    [RelayCommand]
    private void RefreshDevices()
    {
        _state = _audio.ReadState();
        RefreshDeviceViews();
        CompareNow();
    }

    private void OnDevicesChanged()
    {
        if (_disposed) return;
        RefreshDevices();
        if (MonitorEnabled && _sink is null) EnsureMonitor();
        else UpdateMonitorStatus();
        if (_mixPurpose == MixPurpose.None) EnsureCable();
        else if (_mixPurpose == MixPurpose.Cable && !Cable.IsReady) EnsureCable(); // stops and says why
        else if (_mixPurpose == MixPurpose.Cable && _mix is { HasVoice: false } && _profile.MicDeviceId is { } micId
                 && _state.FindById(micId)?.Status == DeviceStatus.Active)
            RestartMix(); // the microphone is back: take it into the cable again
        UpdateCableStatus();
    }

    private void RefreshDeviceViews()
    {
        Devices.Clear();
        foreach (var d in _state.Devices.Where(d => ShowNotPresent || d.Status != DeviceStatus.NotPresent))
            Devices.Add(DeviceRowViewModel.From(d));
        int render = _state.Devices.Count(d => d.Flow == DeviceFlow.Render && d.Status == DeviceStatus.Active);
        int capture = _state.Devices.Count(d => d.Flow == DeviceFlow.Capture && d.Status == DeviceStatus.Active);
        DevicesHeader = L.F("Devices.Header", render, capture);
        NotPresentCount = _state.Devices.Count(d => d.Status == DeviceStatus.NotPresent);
        NotPresentText = L.F("Devices.ShowNotPresent", NotPresentCount);

        var defaultRender = _state.GetDefault(DeviceFlow.Render, DeviceRoleKind.Multimedia);
        _syncingSelection = true;
        try
        {
            OutputChoices.Clear();
            foreach (var d in _state.Devices.Where(d => d.Flow == DeviceFlow.Render && d.Status == DeviceStatus.Active && !CableDetector.IsCableName(d.Name)))
                OutputChoices.Add(new OutputChoice(d.Id, d.Name, true, d.Id == defaultRender?.Id));

            var id = _profile.MonitorDeviceId;
            if (id is not null && OutputChoices.All(c => c.Id != id))
            {
                var known = _state.FindById(id);
                OutputChoices.Add(new OutputChoice(id, known?.Name ?? _profile.MonitorDeviceName ?? L.Get("Main.ChosenDevice"), false, false));
            }
            SelectedOutput = OutputChoices.FirstOrDefault(c => c.Id == id);
        }
        finally
        {
            _syncingSelection = false;
        }

        RefreshMicChoices();
        RefreshCable();
    }

    partial void OnShowNotPresentChanged(bool value) => RefreshDeviceViews();

    partial void OnSelectedOutputChanged(OutputChoice? value)
    {
        if (_syncingSelection || value is null) return;
        _profile.MonitorDeviceId = value.Id;
        _profile.MonitorDeviceName = value.Name;
        ScheduleSave();
        EnsureMonitor();
    }

    // ---------------------------------------------------------------- monitor output

    partial void OnMonitorEnabledChanged(bool value)
    {
        if (_profile.MonitorEnabled != value)
        {
            _profile.MonitorEnabled = value;
            ScheduleSave();
        }
        if (_initialized) EnsureMonitor();
    }

    partial void OnMasterVolumePercentChanged(double value)
    {
        float v = (float)Math.Clamp(value / 100.0, 0, 1);
        if (_player is not null) _player.MasterVolume = v;
        if (Math.Abs(_profile.MasterVolume - v) > 0.0001f)
        {
            _profile.MasterVolume = v;
            ScheduleSave();
        }
    }

    partial void OnOverlayEnabledChanged(bool value)
    {
        var mode = value ? OverlapMode.Overlay : OverlapMode.StopPrevious;
        if (_player is not null) _player.Overlap = mode;
        if (_profile.Overlap != mode)
        {
            _profile.Overlap = mode;
            ScheduleSave();
        }
    }

    private TargetFormat? ReadMonitorFormat()
    {
        if (_profile.MonitorDeviceId is null) return null;
        var mix = _audio.ReadMixFormat(_profile.MonitorDeviceId);
        return mix is null ? null : new TargetFormat(mix.SampleRate, mix.Channels);
    }

    private PlayerGroup CreatePlayer(TargetFormat format) => new(format)
    {
        MasterVolume = (float)Math.Clamp(_profile.MasterVolume, 0f, 1f),
        Overlap = _profile.Overlap,
    };

    /// <summary>(Re)starts the monitor output according to the current settings.</summary>
    private void EnsureMonitor()
    {
        StopSink();

        if (!MonitorEnabled)
        {
            ClearNotice("monitor");
            UpdateMonitorStatus();
            return;
        }

        var id = _profile.MonitorDeviceId;
        if (id is null)
        {
            ShowNotice("monitor", T("Notice.MonitorNone"));
            UpdateMonitorStatus();
            return;
        }

        IAudioOutput? sink = null;
        try
        {
            sink = _audio.OpenOutput(id, 30);
            if (sink is null)
            {
                var name = _profile.MonitorDeviceName;
                ShowNotice("monitor", () => L.F("Notice.MonitorMissing", name ?? L.Get("Main.TheChosenDevice")));
                UpdateMonitorStatus();
                return;
            }
            if (sink.SourceFormat != _player.Format) SwitchFormat(sink.SourceFormat);
            _player.SetMonitorActive(true);
            sink.Stopped += OnSinkStopped;
            sink.Start(_player.Monitor);
            _sink = sink;
            ClearNotice("monitor");
            ClearNotice("play");
        }
        catch (SinkException ex)
        {
            sink?.Dispose();
            _player.SetMonitorActive(false);
            ShowNotice("monitor", TF("Notice.MonitorSinkError", ex.Message), T("Action.Retry"), EnsureMonitor);
        }
        catch (COMException ex)
        {
            sink?.Dispose();
            _player.SetMonitorActive(false);
            ShowNotice("monitor", TF("Notice.MonitorNoAnswer", ex.Message), T("Action.Retry"), EnsureMonitor);
        }
        UpdateMonitorStatus();
    }

    private void StopSink()
    {
        var sink = _sink;
        _sink = null;
        if (sink is not null)
        {
            sink.Stopped -= OnSinkStopped;
            sink.Dispose();
        }
        // No audio thread reads the player any more.
        _player?.SetMonitorActive(false);
    }

    private void OnSinkStopped(object? sender, Exception? error)
    {
        if (!ReferenceEquals(sender, _sink)) return;
        StopSink();
        var name = _profile.MonitorDeviceName;
        var why = error is null ? "" : $" ({error.Message})";
        ShowNotice("monitor", () => L.F("Notice.MonitorStopped", name ?? L.Get("Main.TheMonitorDevice"), why), T("Action.Retry"), EnsureMonitor);
        UpdateMonitorStatus();
    }

    /// <summary>The monitor device has another mix format: decode all sounds again for it.</summary>
    private void SwitchFormat(TargetFormat format)
    {
        StopMix(L.Get("Main.ReloadingSounds"));
        _player = CreatePlayer(format);
        _bank.Reset(format);
        foreach (var tile in Tiles)
        {
            tile.Clip = null;
            _ = LoadTileAsync(tile);
        }
        RestartMix();
    }

    private void UpdateMonitorStatus()
    {
        var name = SelectedOutput?.Name ?? _profile.MonitorDeviceName ?? L.Get("Main.NoDevice");
        StatusMonitor = !MonitorEnabled ? L.Get("Status.MonitorOff")
            : _sink is null ? L.F("Status.MonitorBroken", name)
            : L.F("Status.MonitorOn", name, _player.Format.SampleRate, _player.Format.Channels);
    }

    // ---------------------------------------------------------------- tiles

    private async Task LoadTileAsync(TileViewModel tile)
    {
        int version = ++tile.LoadVersion;
        tile.State = TileState.Loading;
        tile.ErrorText = null;
        var path = Path.Combine(_paths.SoundsDir, tile.Config.FileName);
        try
        {
            var clip = await _bank.LoadAsync(tile.Config.Id, path);
            if (version != tile.LoadVersion || clip.Format != _player.Format) return; // superseded
            tile.Clip = clip;
            tile.State = TileState.Ready;
            tile.SetIssue(null);
            _player.SetVolume(clip.Handle, tile.Volume);
        }
        catch (SoundLoadException ex)
        {
            if (version != tile.LoadVersion) return;
            tile.Clip = null;
            tile.ErrorText = ex.Message;
            tile.State = TileState.Error;
            SetLoadIssue(tile);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version != tile.LoadVersion) return;
            tile.Clip = null;
            tile.ErrorText = L.F("Pad.LoadFailed", ex.Message);
            tile.State = TileState.Error;
            SetLoadIssue(tile);
        }
        tile.RefreshStatus();
    }

    private void SetLoadIssue(TileViewModel tile)
    {
        var error = tile.ErrorText;
        tile.SetIssue(() => L.F("Pad.LoadIssue", error), T("Action.RemovePad"), () => RemoveTile(tile));
    }

    public void OnTileClicked(TileViewModel tile)
    {
        if (tile.IsRenaming) return;
        switch (tile.State)
        {
            case TileState.Loading:
                ShowNotice("play", TF("Notice.StillLoading", tile.Name));
                return;
            case TileState.Error:
                tile.IsIssueOpen = true;
                return;
        }

        if (tile.IsPlaying)
        {
            _player.Stop(tile.Clip!.Handle);
            tile.IsPlaying = false;
            tile.PlayRequestedAt = default;
            return;
        }

        if (!_player.AnyActive)
        {
            ShowNotice("play", T(MonitorEnabled ? "Notice.NowhereMonitorBroken" : "Notice.NowhereMonitorOff"),
                MonitorEnabled ? null : T("Action.MonitorOn"), MonitorEnabled ? null : () => MonitorEnabled = true);
            return;
        }
        ClearNotice("play");

        _player.Play(tile.Clip!, tile.Volume);
        tile.PlayRequestedAt = DateTime.UtcNow;
        tile.IsPlaying = true;
        tile.Progress = 0;
        if (!OverlayEnabled)
        {
            foreach (var other in Tiles)
                if (!ReferenceEquals(other, tile)) other.IsPlaying = false;
        }
    }

    public void OnTileVolumeChanged(TileViewModel tile)
    {
        float v = tile.Volume;
        if (Math.Abs(tile.Config.Volume - v) < 0.0001f) return;
        tile.Config.Volume = v;
        if (tile.Clip is not null) _player?.SetVolume(tile.Clip.Handle, v);
        ScheduleSave();
    }

    [RelayCommand]
    private void StopAll()
    {
        _player.StopAll();
        foreach (var t in Tiles)
        {
            t.IsPlaying = false;
            t.PlayRequestedAt = default;
        }
    }

    private void UpdateProgress()
    {
        UpdateMixStatus();
        var now = DateTime.UtcNow;
        foreach (var t in Tiles)
        {
            if (t.Clip is null)
            {
                t.IsPlaying = false;
                continue;
            }
            var p = _player.GetProgress(t.Clip.Handle);
            if (p is null && (now - t.PlayRequestedAt).TotalMilliseconds < 250 && _player.AnyActive)
                continue; // the audio thread has not picked up the start yet
            t.IsPlaying = p is not null;
            t.Progress = p ?? 0;
        }
    }

    /// <summary>Adds sound files (from drag &amp; drop or the file dialog). Folders are searched one level deep.</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                try { files.AddRange(Directory.EnumerateFiles(p).Order()); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            else
            {
                files.Add(p);
            }
        }

        var direct = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();
        var failed = new List<string>();
        int added = 0;
        bool pictureChosen = false;
        foreach (var file in files)
        {
            if (Soundboard.Core.Images.ImageFiles.HasImageExtension(file))
            {
                // Pictures in a dropped folder are skipped quietly; a picture picked as a sound gets a hint.
                if (direct.Contains(file)) pictureChosen = true;
                continue;
            }
            if (!SoundDecoder.IsSupported(file))
            {
                rejected.Add(Path.GetFileName(file));
                continue;
            }
            string fileName;
            try
            {
                fileName = _paths.ImportSound(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{Path.GetFileName(file)} ({ex.Message})");
                continue;
            }

            var config = new TileConfig
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FileName = fileName,
                Volume = 1f,
                Color = Soundboard.Core.Design.PadPalette.ForIndex(_profile.Tiles.Count),
            };
            _profile.Tiles.Add(config);
            var tile = new TileViewModel(this, config);
            Tiles.Add(tile);
            _ = LoadTileAsync(tile);
            added++;
        }

        if (added > 0) ScheduleSave();
        if (rejected.Count > 0)
            ShowNotice("rejected", TF("Notice.Rejected", string.Join(", ", rejected)));
        else
            ClearNotice("rejected");
        if (failed.Count > 0)
            ShowNotice("copy", TF("Notice.CopyFailed", string.Join(", ", failed)));
        if (pictureChosen)
            ShowNotice("image-many", T("Notice.PictureNotSound"));
    }

    [RelayCommand]
    private void AddFilesDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = L.Get("Dialog.AddSounds"),
            Multiselect = true,
            Filter = L.Get("Dialog.SoundFilter"),
        };
        if (dlg.ShowDialog() == true) AddFiles(dlg.FileNames);
    }

    public void RemoveTile(TileViewModel tile)
    {
        int index = Tiles.IndexOf(tile);
        if (index < 0) return;
        if (tile.Clip is not null) _player.Stop(tile.Clip.Handle);
        Tiles.RemoveAt(index);
        int configIndex = _profile.Tiles.IndexOf(tile.Config);
        _profile.Tiles.Remove(tile.Config);
        _bank.Remove(tile.Config.Id);
        _paths.ReleaseImage(tile.Config.Image); // deleted later unless "Undo" brings the pad back
        _hotkeys.Remove(tile.Config.Id.ToString());
        ClearNotice("hotkey-" + tile.Config.Id);
        RefreshHotkeyViews();
        ScheduleSave();

        ShowNotice("removed", TF("Notice.PadRemoved", tile.Name), T("Action.Undo"), () =>
        {
            _profile.Tiles.Insert(Math.Clamp(configIndex, 0, _profile.Tiles.Count), tile.Config);
            var restored = new TileViewModel(this, tile.Config);
            Tiles.Insert(Math.Clamp(index, 0, Tiles.Count), restored);
            _ = LoadTileAsync(restored);
            _ = LoadTileImageAsync(restored);
            _hotkeys.Set(restored.Config.Id.ToString(), HotkeyGesture.Parse(restored.Config.Hotkey));
            RefreshHotkeyViews();
            ScheduleSave();
        });
    }

    public void ShowTileInFolder(TileViewModel tile)
    {
        var path = Path.Combine(_paths.SoundsDir, tile.Config.FileName);
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else OpenSoundsFolder();
    }

    [RelayCommand]
    private void OpenSoundsFolder()
    {
        Directory.CreateDirectory(_paths.SoundsDir);
        Process.Start("explorer.exe", $"\"{_paths.SoundsDir}\"");
    }

    // ---------------------------------------------------------------- audio state: saved vs. now


    private AudioSnapshot? _snapshot;
    private bool _stateReady;

    public ObservableCollection<ComparisonRow> ComparisonRows { get; } = [];

    [ObservableProperty]
    private string _snapshotHeader = "";

    [ObservableProperty]
    private string _snapshotInfo = "";

    private string? _snapshotNoticeShownFor;

    /// <summary>Explanation above the comparison when a default device changed; empty otherwise.</summary>
    [ObservableProperty]
    private string _snapshotAdvice = "";

    [ObservableProperty]
    private string _restoreResultText = "";

    /// <summary>Latest comparison (null without a snapshot).</summary>
    public SnapshotComparison? Comparison { get; private set; }

    public static string AppVersion =>
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>Start path: running flag, load or take the first snapshot, compare.</summary>
    private void InitAudioState()
    {
        bool crashed = false;
        if (_services.UseDirtyFlag)
        {
            var info = _dirty.Read();
            crashed = info is not null && DirtyFlag.IsStale(info, DirtyFlag.ProcessStartTime);
            WriteDirtyFlag(cableActive: false);
        }

        try
        {
            _snapshot = SnapshotStore.Load(_paths.SnapshotFile);
        }
        catch (SnapshotException ex)
        {
            ShowNotice("snapshot-file", TF("Notice.SnapshotFile", ex.Message), T("Action.SaveState"), SaveSnapshotNow);
        }

        if (_snapshot is null && !File.Exists(_paths.SnapshotFile))
        {
            // First start: remember how Windows audio looks before anything else happens.
            TrySaveSnapshot(_state);
        }

        _stateReady = true;
        CompareNow();

        // After an unclean exit, speak up only if something is actually off: a Windows default
        // device that differs from the saved state. Nothing to do means nothing to say.
        if (crashed && Comparison is { DefaultsChanged: true })
        {
            ShowNotice("crash", T("Notice.CrashChanged"), T("Action.Restore"), RestoreDefaults);
            HideNoticeAfter("crash", TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>Removes the notice shown under <paramref name="key"/> after a delay, unless it
    /// has been replaced or closed in the meantime.</summary>
    private void HideNoticeAfter(string key, TimeSpan delay)
    {
        var shown = Notices.FirstOrDefault(n => n.Key == key);
        if (shown is null) return;
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Notices.Contains(shown)) Notices.Remove(shown);
        };
        timer.Start();
    }

    private void WriteDirtyFlag(bool cableActive)
    {
        if (!_services.UseDirtyFlag) return;
        try
        {
            using var me = Process.GetCurrentProcess();
            _dirty.Write(new DirtyInfo(Environment.ProcessId, me.StartTime, cableActive));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: only the crash detection is missing.
        }
    }

    private bool TrySaveSnapshot(AudioStateReport state)
    {
        try
        {
            var snap = AudioSnapshot.From(state, AppVersion);
            SnapshotStore.Save(_paths.SnapshotFile, snap);
            _snapshot = snap;
            ClearNotice("snapshot-file");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("snapshot-file", TF("Notice.SnapshotSaveFailed", ex.Message), T("Action.Retry"), SaveSnapshotNow);
            return false;
        }
    }

    /// <summary>Compares the snapshot with the current state and shows the result.</summary>
    public void CompareNow()
    {
        if (!_stateReady) return;
        ComparisonRows.Clear();
        if (_snapshot is null)
        {
            Comparison = null;
            SnapshotHeader = L.Get("State.NotSaved");
            SnapshotInfo = L.Get("State.NotSavedInfo");
            SnapshotAdvice = "";
            ClearNotice("snapshot");
            UpdateCableCompare();
            return;
        }

        var cmp = SnapshotService.Compare(_snapshot, _state);
        Comparison = cmp;
        foreach (var r in cmp.Rows) ComparisonRows.Add(r);
        string when = L.DateTime(_snapshot.TakenAt);
        SnapshotHeader = cmp.IsIdentical
            ? L.F("State.Same", when)
            : L.Count("State.Diff", cmp.DifferenceCount, when);
        SnapshotInfo = L.Count("State.Unchanged", cmp.UnchangedDevices);

        if (cmp.DefaultsChanged)
        {
            var lines = string.Join("; ", cmp.DefaultChangeLines);
            SnapshotAdvice = L.F(cmp.CableBecameDefault ? "State.AdviceCable" : "State.Advice", lines);
            int n = cmp.DefaultChangeLines.Count();
            // Shown once per difference: a refresh (e.g. opening the settings) does not bring a closed notice back.
            if (lines != _snapshotNoticeShownFor)
            {
                _snapshotNoticeShownFor = lines;
                bool cable = cmp.CableBecameDefault;
                ShowNotice("snapshot", () => cable ? L.Get("Notice.CableDefault") : L.Count("Notice.DefaultChanged", n),
                    T("Action.View"), () => OpenSettings(SettingsTabs.AudioState));
            }
        }
        else
        {
            SnapshotAdvice = "";
            ClearNotice("snapshot");
            _snapshotNoticeShownFor = null;
        }
        UpdateCableCompare();
    }

    /// <summary>Puts the default devices back to the saved ones (and only those).</summary>
    [RelayCommand]
    private void RestoreDefaults()
    {
        if (_snapshot is null)
        {
            ShowNotice("restore", T("Notice.RestoreNoSnapshot"), T("Action.SaveState"), SaveSnapshotNow);
            return;
        }

        var before = _audio.ReadState();
        var cmp = SnapshotService.Compare(_snapshot, before);
        if (!cmp.DefaultsChanged)
        {
            RestoreResultText = L.Get("Restore.NothingToDo");
            RefreshDevices();
            return;
        }

        var result = SnapshotService.Restore(_snapshot, before, _services.Policy);
        RefreshDevices();
        RestoreResultText = result.RestoredCount == 0 ? L.Get("Restore.None") : L.Count("Restore.Done", result.RestoredCount);
        if (result.AllGood) ClearNotice("restore");
        else
            ShowNotice("restore", () => L.F("Notice.RestorePartly", result.ManualSteps().Replace(Environment.NewLine, "; ").Replace("\n", "; ")));
    }

    /// <summary>Saves the current Windows audio state as the new reference.</summary>
    [RelayCommand]
    private void SaveSnapshotNow()
    {
        RefreshDevices();
        if (TrySaveSnapshot(_state))
        {
            RestoreResultText = L.F("State.SavedNow", L.DateTime(DateTime.Now));
            CompareNow();
        }
    }

    [RelayCommand]
    private void CompareAgain()
    {
        RefreshDevices();
        RestoreResultText = Comparison is null ? "" : L.Get(Comparison.IsIdentical ? "State.ComparedSame" : "State.ComparedDiff");
    }

    // ---------------------------------------------------------------- microphone and mix

    private enum MixPurpose
    {
        None,
        FileTest,
        Cable,
    }

    private static readonly TimeSpan TestLength = TimeSpan.FromSeconds(10);

    private MixSession? _mix;
    private MixPurpose _mixPurpose;
    private FileSink? _fileSink;
    private string? _lastTestFile;
    private bool _syncingMic;

    public ObservableCollection<OutputChoice> MicChoices { get; } = [];

    public IReadOnlyList<BufferChoice> BufferChoices { get; } =
        [.. Enum.GetValues<BufferSize>().Select(b => new BufferChoice(b))];

    [ObservableProperty]
    private OutputChoice? _selectedMic;

    [ObservableProperty]
    private BufferChoice? _selectedBuffer;

    [ObservableProperty]
    private bool _blockVoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MicVolumeText))]
    private double _micVolumePercent;

    /// <summary>Level of what the mix sends right now, 0..1.</summary>
    [ObservableProperty]
    private double _mixLevel;

    [ObservableProperty]
    private string _testStatus = "";

    [ObservableProperty]
    private bool _hasTestFile;

    [ObservableProperty]
    private string _statusMix = "";

    private void InitMixSettings()
    {
        SelectedBuffer = BufferChoices.First(b => b.Size == _profile.Buffer);
        BlockVoice = _profile.BlockVoiceWhileSound;
        MicVolumePercent = Math.Round(Math.Clamp(_profile.MicVolume, 0f, 2f) * 100);
    }

    /// <summary>Microphone list: active capture endpoints, without virtual cables (that would feed back).</summary>
    private void RefreshMicChoices()
    {
        if (_profile.MicDeviceId is null)
        {
            // First start: the current Windows default microphone, only read, remembered by ID.
            var def = _state.GetDefault(DeviceFlow.Capture, DeviceRoleKind.Communications) ?? _state.GetDefault(DeviceFlow.Capture, DeviceRoleKind.Console);
            if (def?.Id is not null && !CableDetector.IsCableName(def.Name))
            {
                _profile.MicDeviceId = def.Id;
                _profile.MicDeviceName = def.Name;
                ScheduleSave();
            }
        }

        var defaultMic = _state.GetDefault(DeviceFlow.Capture, DeviceRoleKind.Communications);
        _syncingMic = true;
        try
        {
            MicChoices.Clear();
            foreach (var d in _state.Devices.Where(d => d.Flow == DeviceFlow.Capture && d.Status == DeviceStatus.Active && !CableDetector.IsCableName(d.Name)))
                MicChoices.Add(new OutputChoice(d.Id, d.Name, true, d.Id == defaultMic?.Id));
            var id = _profile.MicDeviceId;
            if (id is not null && MicChoices.All(c => c.Id != id))
                MicChoices.Add(new OutputChoice(id, _state.FindById(id)?.Name ?? _profile.MicDeviceName ?? L.Get("Main.ChosenMic"), false, false));
            SelectedMic = MicChoices.FirstOrDefault(c => c.Id == id);
        }
        finally
        {
            _syncingMic = false;
        }
    }

    partial void OnSelectedMicChanged(OutputChoice? value)
    {
        if (_syncingMic || value is null || value.Id == _profile.MicDeviceId) return;
        _profile.MicDeviceId = value.Id;
        _profile.MicDeviceName = value.Name;
        ScheduleSave();
        if (_mixPurpose == MixPurpose.Cable) RestartMix();
    }

    partial void OnSelectedBufferChanged(BufferChoice? value)
    {
        if (value is null || value.Size == _profile.Buffer) return;
        _profile.Buffer = value.Size;
        ScheduleSave();
        if (_mixPurpose == MixPurpose.Cable) RestartMix();
    }

    partial void OnBlockVoiceChanged(bool value)
    {
        if (_mix is not null) _mix.Mixer.BlockVoiceWhileSound = value;
        if (_profile.BlockVoiceWhileSound == value) return;
        _profile.BlockVoiceWhileSound = value;
        ScheduleSave();
    }

    partial void OnMicVolumePercentChanged(double value)
    {
        float v = (float)Math.Clamp(value / 100.0, 0, 2);
        if (_mix is not null) _mix.Mixer.VoiceVolume = v;
        if (Math.Abs(_profile.MicVolume - v) < 0.0001f) return;
        _profile.MicVolume = v;
        ScheduleSave();
    }

    /// <summary>Opens the chosen microphone. Null plus a notice if that is not possible.</summary>
    private ICaptureInput? OpenMic(string noticeKey)
    {
        var id = _profile.MicDeviceId;
        if (id is null)
        {
            ShowNotice(noticeKey, T("Notice.MicNone"), T("Action.ChooseMic"), () => OpenSettings(SettingsTabs.MicAndCable));
            return null;
        }
        try
        {
            var mic = _audio.OpenCapture(id, BufferSizes.Milliseconds(_profile.Buffer));
            if (mic is null)
            {
                var name = _profile.MicDeviceName;
                ShowNotice(noticeKey, () => L.F("Notice.MicMissing", name ?? L.Get("Main.TheChosenMic")), T("Action.ChooseMic"), () => OpenSettings(SettingsTabs.MicAndCable));
            }
            return mic;
        }
        catch (SinkException ex)
        {
            ShowNotice(noticeKey, TF("Notice.MicError", ex.Message), T("Action.ChooseMic"), () => OpenSettings(SettingsTabs.MicAndCable));
            return null;
        }
    }

    /// <summary>Starts a mix session on <paramref name="output"/>. Takes ownership of both devices.</summary>
    private bool StartMix(IAudioOutput output, ICaptureInput? mic, MixPurpose purpose, string noticeKey)
    {
        MixSession? session = null;
        try
        {
            session = new MixSession(output, mic, _player.Mix, _profile.Buffer);
            session.Mixer.BlockVoiceWhileSound = BlockVoice;
            session.Mixer.VoiceVolume = (float)(MicVolumePercent / 100.0);
            session.Stopped += OnMixStopped;
            _player.SetMixActive(true);
            _mix = session;
            _mixPurpose = purpose;
            session.Start();
            return true;
        }
        catch (Exception ex) when (ex is SinkException or COMException or ArgumentException or InvalidOperationException)
        {
            _mix = null;
            _mixPurpose = MixPurpose.None;
            if (session is not null) session.Dispose();
            else
            {
                output.Dispose();
                mic?.Dispose();
            }
            _player.SetMixActive(false); // after the output has stopped
            var message = ex.Message;
            ShowNotice(noticeKey, ex is SinkException ? () => message : TF("Notice.OutputStartFailed", message), T("Action.Retry"), purpose == MixPurpose.FileTest ? RecordTest : RestartMix);
            return false;
        }
    }

    /// <summary>Stops the running mix. <paramref name="reason"/> is shown if a test recording is cut short.</summary>
    private void StopMix(string? reason = null)
    {
        var session = _mix;
        var purpose = _mixPurpose;
        _mix = null;
        _mixPurpose = MixPurpose.None;
        _fileSink = null;
        if (session is not null)
        {
            session.Stopped -= OnMixStopped;
            session.Dispose();
        }
        _player.SetMixActive(false);
        MixLevel = 0;
        UpdateCableStatus();
        if (purpose == MixPurpose.FileTest && reason is not null)
        {
            TestStatus = "";
            ShowNotice("test", TF("Notice.TestAborted", reason), T("Action.RecordAgain"), RecordTest);
        }
    }

    private void OnMixStopped(object? sender, MixStoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, _mix)) return;
        var purpose = _mixPurpose;
        if (purpose == MixPurpose.FileTest && !e.Microphone && e.Error is null)
        {
            var file = _lastTestFile;
            StopMix();
            TestStatus = L.F("Test.Done", Path.GetFileName(file));
            HasTestFile = true;
            ClearNotice("test");
            RestartMix();
            return;
        }

        var why = e.Error is null ? "" : $" ({e.Error.Message})";
        if (e.Microphone)
        {
            var name = _profile.MicDeviceName;
            bool denied = e.Error is UnauthorizedAccessException;
            Func<string> text = () => denied
                ? WasapiAudioSystem.MicrophoneAccessHint
                : L.F("Notice.MicStopped", name ?? L.Get("Main.TheMic"), why);
            StopMix(purpose == MixPurpose.FileTest ? text() : null);
            // The cable goes on with the sounds; the microphone joins again when it is back.
            EnsureCable(withMic: false);
            ShowNotice("mic", text, T("Action.Retry"), RestartMix);
        }
        else
        {
            StopMix(L.F("Notice.OutputInterrupted", why));
            if (purpose == MixPurpose.Cable)
                ShowNotice("cable", TF("Notice.CableInterrupted", why), T("Action.Retry"), RestartMix);
            else
                RestartMix(); // test recording failed: the cable takes the mix back
        }
    }

    /// <summary>Restarts the permanent mix output (if one is configured) after a change.</summary>
    private void RestartMix() => EnsureCable();

    /// <summary>Records microphone + sounds for 10 seconds into a WAV file (test without cable).</summary>
    [RelayCommand]
    private void RecordTest()
    {
        if (_mixPurpose == MixPurpose.FileTest && _fileSink is not null)
        {
            int left = (int)Math.Ceiling(TestLength.TotalSeconds * (1 - _fileSink.Progress));
            ShowNotice("test", TF("Notice.TestRunning", left));
            return;
        }

        var mic = OpenMic("test");
        if (mic is null) return;
        StopMix();
        ClearNotice("test");

        Directory.CreateDirectory(_paths.RecordingsDir);
        _lastTestFile = Path.Combine(_paths.RecordingsDir, $"test-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
        var sink = new FileSink(_lastTestFile, new TargetFormat(48000, 2), TestLength);
        if (StartMix(sink, mic, MixPurpose.FileTest, "test"))
        {
            _fileSink = sink;
            HasTestFile = false;
            TestStatus = L.F("Test.Running", (int)TestLength.TotalSeconds);
        }
        else
        {
            RestartMix(); // the cable was stopped for the test; take it back
        }
    }

    [RelayCommand]
    private void ShowTestFile()
    {
        if (_lastTestFile is not null && File.Exists(_lastTestFile)) Process.Start("explorer.exe", $"/select,\"{_lastTestFile}\"");
        else
        {
            Directory.CreateDirectory(_paths.RecordingsDir);
            Process.Start("explorer.exe", $"\"{_paths.RecordingsDir}\"");
        }
    }

    /// <summary>Meter, countdown and drop-out counter; called by the 40-ms timer.</summary>
    private void UpdateMixStatus()
    {
        var mix = _mix;
        if (mix is null)
        {
            if (MixLevel != 0) MixLevel = 0;
        }
        else
        {
            double peak = mix.Mixer.TakePeak();
            MixLevel = Math.Max(peak, MixLevel * 0.85);
        }

        if (_mixPurpose == MixPurpose.FileTest && _fileSink is not null)
        {
            int left = (int)Math.Ceiling(TestLength.TotalSeconds * (1 - _fileSink.Progress));
            TestStatus = L.F("Test.Running", left);
        }

        string text = mix is null || !mix.HasVoice
            ? L.Get("Status.MicOff")
            : mix.Underruns == 0 ? L.Get("Status.MicOn")
            : L.Count("Status.MicDropouts", mix.Underruns);
        if (text != StatusMix) StatusMix = text;
    }

    // ---------------------------------------------------------------- virtual cable

    public const string CableDownloadUrl = "https://vb-cable.com";

    /// <summary>The cable as found in the last device read.</summary>
    public CableInfo Cable { get; private set; } = new(null, null);

    [ObservableProperty]
    private bool _cableEnabled;

    [ObservableProperty]
    private bool _cableFound;

    [ObservableProperty]
    private string _cableStatusText = "";

    [ObservableProperty]
    private string _statusCable = "";

    /// <summary>What the comparison says about the driver installation (setup step 1).</summary>
    [ObservableProperty]
    private string _cableCompareText = "";

    [ObservableProperty]
    private bool _cableCompareProblem;

    /// <summary>True while microphone + sounds really go to the cable.</summary>
    public bool CableRunning => _mixPurpose == MixPurpose.Cable && _mix is not null;

    partial void OnCableEnabledChanged(bool value)
    {
        if (_profile.CableEnabled == value) return;
        _profile.CableEnabled = value;
        ScheduleSave();
        EnsureCable();
    }

    private void RefreshCable()
    {
        Cable = CableDetector.Detect(_state);
        CableFound = Cable.IsReady;
        CableStatusText = Cable.IsReady
            ? L.F("Cable.Found", Cable.Input!.Name, Cable.Output!.Name)
            : L.Get(Cable.IsKnown ? "Cable.Disabled" : "Cable.None");
        UpdateCableCompare();
    }

    private void UpdateCableCompare()
    {
        var cmp = Comparison;
        if (cmp is null)
        {
            CableCompareText = L.Get("Cable.NoSnapshot");
            CableCompareProblem = true;
            return;
        }
        string when = L.DateTime(cmp.Snapshot.TakenAt);
        bool cableNew = cmp.Rows.Any(r => r.Kind == ComparisonKind.Added && CableDetector.IsCableName(r.What));
        if (cmp.DefaultsChanged)
        {
            CableCompareText = L.F("Cable.DefaultsChanged", when, string.Join("; ", cmp.DefaultChangeLines));
            CableCompareProblem = true;
        }
        else
        {
            CableCompareText = L.F(cableNew ? "Cable.OnlyCableNew" : "Cable.Unchanged", when);
            CableCompareProblem = false;
        }
    }

    /// <summary>Starts or stops "microphone + sounds → cable" according to settings and devices.</summary>
    private void EnsureCable() => EnsureCable(withMic: true);

    /// <param name="withMic">False after a microphone failure: the cable goes on with the sounds only, so a
    /// broken microphone cannot cause a restart loop. It joins again on the next device change.</param>
    private void EnsureCable(bool withMic)
    {
        if (!_initialized || _disposed) return;
        if (_mixPurpose == MixPurpose.FileTest) return; // the test recording has the mix; the cable follows afterwards
        if (_mixPurpose == MixPurpose.Cable) StopMix();

        if (!CableEnabled)
        {
            ClearNotice("cable");
            UpdateCableStatus();
            return;
        }
        if (!Cable.IsReady)
        {
            // No banner: the status line says "No cable" with a warning sign, and a click on it
            // opens the setup, which explains the way.
            ClearNotice("cable");
            UpdateCableStatus();
            return;
        }

        IAudioOutput? output;
        try
        {
            output = _audio.OpenOutput(Cable.Input!.Id, BufferSizes.Milliseconds(_profile.Buffer));
        }
        catch (SinkException ex)
        {
            ShowNotice("cable", TF("Notice.CableOpenError", ex.Message), T("Action.Retry"), EnsureCable);
            UpdateCableStatus();
            return;
        }
        if (output is null)
        {
            ShowNotice("cable", T("Notice.CableInputFailed"), T("Action.Retry"), EnsureCable);
            UpdateCableStatus();
            return;
        }

        // Without a microphone the cable still carries the sounds; the mic notice says what is missing.
        var mic = withMic ? OpenMic("mic") : null;
        if (mic is not null) ClearNotice("mic");
        if (StartMix(output, mic, MixPurpose.Cable, "cable"))
        {
            ClearNotice("cable");
            WriteDirtyFlag(cableActive: true);
        }
        UpdateCableStatus();
    }

    private void UpdateCableStatus()
    {
        StatusCable = L.Get(!CableEnabled ? "Status.CableOff"
            : !Cable.IsReady ? "Status.CableMissing"
            : CableRunning ? "Status.CableSending"
            : _mixPurpose == MixPurpose.FileTest ? "Status.CablePaused"
            : "Status.CableBroken");
        StatusCableTip = L.Get(CableEnabled && !Cable.IsReady ? "Status.CableMissingTip" : "Status.CableTip");
        StatusCableTab = CableEnabled && !Cable.IsReady ? SettingsTabs.Setup : SettingsTabs.MicAndCable;
        OnPropertyChanged(nameof(CableRunning));
        CableWarn = CableEnabled && !CableRunning && _mixPurpose != MixPurpose.FileTest;
        if (!CableRunning) WriteDirtyFlag(cableActive: false);
    }

    /// <summary>One second of 440 Hz into the cable only (not on the headphones).</summary>
    [RelayCommand]
    private void PlayTestTone()
    {
        if (!CableRunning)
        {
            ShowNotice("tone", T(!CableEnabled ? "Notice.ToneCableOff" : !Cable.IsReady ? "Notice.ToneNoCable" : "Notice.ToneNotRunning"),
                T(!CableEnabled ? "Action.CableOn" : !Cable.IsReady ? "Action.OpenSetup" : "Action.Retry"),
                !CableEnabled ? () => CableEnabled = true : !Cable.IsReady ? OpenSetup : RestartMix);
            return;
        }
        ClearNotice("tone");
        _mix!.Mixer.PlayTestTone();
    }

    [RelayCommand]
    private void OpenCableWebsite() => OpenUrl(CableDownloadUrl);

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    // ---------------------------------------------------------------- settings panel

    /// <summary>The settings cover the pads while open; the header with "Stop" stays.</summary>
    [ObservableProperty]
    private bool _settingsOpen;

    /// <summary>Open tab, one of <see cref="SettingsTabs"/>.</summary>
    [ObservableProperty]
    private string _settingsTab = SettingsTabs.Setup;

    /// <summary>1 = cable, 2 = Discord, 3 = noise suppression.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetupStep1), nameof(IsSetupStep2), nameof(IsSetupStep3), nameof(SetupStepText))]
    private int _setupStep = 1;

    /// <summary>Step as text, for the step buttons ("1", "2", "3").</summary>
    public string SetupStepText
    {
        get => SetupStep.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, out int n) && n is >= 1 and <= 3) SetupStep = n;
        }
    }

    public bool IsSetupStep1 => SetupStep == 1;
    public bool IsSetupStep2 => SetupStep == 2;
    public bool IsSetupStep3 => SetupStep == 3;

    public void OpenSettings(string tab)
    {
        RefreshDevices();
        RefreshAutostart(); // the installer or another copy may have changed the entry
        if (tab == SettingsTabs.Setup)
        {
            ClearNotice("cable"); // the setup says the same in detail
            if (!Cable.IsReady) SetupStep = 1;
        }
        if (tab == SettingsTabs.AudioState) ClearNotice("snapshot"); // the tab shows the same in detail
        SettingsTab = tab;
        SettingsOpen = true;
    }

    partial void OnSettingsTabChanged(string value)
    {
        if (SettingsOpen && value == SettingsTabs.AudioState) ClearNotice("snapshot");
    }

    /// <summary>Opens the settings on the given tab (parameter: tab id; empty = last tab).</summary>
    [RelayCommand]
    private void ShowSettings(string? tab) => OpenSettings(string.IsNullOrEmpty(tab) ? SettingsTab : tab);

    /// <summary>Setup steps (cable, Discord, noise suppression) — tray menu and notices.</summary>
    [RelayCommand]
    private void OpenSetup() => OpenSettings(SettingsTabs.Setup);

    [RelayCommand]
    private void CloseSettings()
    {
        SettingsOpen = false;
        if (!_profile.SetupSeen)
        {
            _profile.SetupSeen = true;
            ScheduleSave();
        }
    }

    [RelayCommand]
    private void GoToStep(string step)
    {
        if (int.TryParse(step, out int n) && n is >= 1 and <= 3) SetupStep = n;
    }

    /// <summary>Setup step 2: which microphone Discord used before (free text).</summary>
    public string DiscordPreviousMic
    {
        get => _profile.DiscordPreviousMic ?? "";
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (v == _profile.DiscordPreviousMic) return;
            _profile.DiscordPreviousMic = v;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    /// <summary>Setup step 3: the user confirmed the Discord voice settings.</summary>
    public bool DiscordSettingsDone
    {
        get => _profile.DiscordSettingsDone;
        set
        {
            if (value == _profile.DiscordSettingsDone) return;
            _profile.DiscordSettingsDone = value;
            ScheduleSave();
            OnPropertyChanged();
        }
    }

    /// <summary>Text for "the app is gone, what now in Discord" (exit dialog, watcher, uninstall).</summary>
    public static string DiscordWayBack(string? previousMic) =>
        string.IsNullOrWhiteSpace(previousMic)
            ? L.Get("Discord.WayBackDefault")
            : L.F("Discord.WayBack", previousMic);

    public string? PreviousDiscordMic => _profile.DiscordPreviousMic;

    public bool TrayHintShown
    {
        get => _profile.TrayHintShown;
        set
        {
            if (value == _profile.TrayHintShown) return;
            _profile.TrayHintShown = value;
            ScheduleSave();
        }
    }

    /// <summary>Text for the exit question: what Discord gets afterwards and how the audio state looks.</summary>
    public string ExitQuestion()
    {
        RefreshDevices();
        var sb = new System.Text.StringBuilder();
        if (CableRunning)
        {
            sb.AppendLine(L.Get("Exit.Silence"));
            sb.AppendLine(DiscordWayBack(_profile.DiscordPreviousMic));
            sb.AppendLine();
        }
        var cmp = Comparison;
        if (cmp is null) sb.AppendLine(L.Get("Exit.NoSnapshot"));
        else if (!cmp.DefaultsChanged) sb.AppendLine(L.Get("Exit.Same"));
        else
        {
            sb.AppendLine(L.Get("Exit.Changed"));
            foreach (var line in cmp.DefaultChangeLines) sb.AppendLine("  " + line);
            sb.AppendLine(L.Get("Exit.NotUs"));
        }
        sb.AppendLine();
        sb.Append(L.Get("Exit.Question"));
        return sb.ToString();
    }

    [RelayCommand]
    private void SearchCable()
    {
        RefreshDevices();
        if (_mixPurpose == MixPurpose.None) EnsureCable();
        RestoreResultText = "";
    }

    // ---------------------------------------------------------------- hotkeys

    private const string StopAllOwner = "stop-all";

    [ObservableProperty]
    private bool _hotkeysPaused;

    [ObservableProperty]
    private string _statusHotkeys = "";

    /// <summary>Rows of the function keys in the keys tab, "Stop all sounds" first.</summary>
    public ObservableCollection<FunctionKeyViewModel> FunctionKeys { get; } = [];

    /// <summary>The key for "Stop all sounds" (default Pause, may be none).</summary>
    public FunctionKeyViewModel StopKey { get; private set; } = null!;

    /// <summary>Pad or function row currently waiting for a key press, or null.</summary>
    public IKeyTarget? Capturing { get; private set; }

    /// <summary>The suggestion shown while waiting; Enter takes only this one.</summary>
    private HotkeyGesture? _captureSuggestion;

    /// <summary>A key the user just tried to give to a function row, but which belongs to someone else.</summary>
    private readonly Dictionary<string, Func<string>> _refusedKeys = [];

    public string StopAllToolTip => HotkeyGesture.Parse(_profile.StopAllHotkey) is { } g
        ? L.F("Main.StopTipKey", g.Display)
        : L.Get("Main.StopTip");

    private void InitFunctionKeys()
    {
        StopKey = new FunctionKeyViewModel(this, StopAllOwner, "Keys.StopAll");
        FunctionKeys.Add(StopKey);
    }

    private bool IsFunctionOwner(string owner) => FunctionKeys.Any(f => f.OwnerId == owner);

    private string? FunctionKeyToken(string owner) => owner switch
    {
        StopAllOwner => _profile.StopAllHotkey,
        _ => null,
    };

    private void SetFunctionKeyToken(string owner, string? token)
    {
        switch (owner)
        {
            case StopAllOwner: _profile.StopAllHotkey = token; break;
        }
    }

    /// <summary>Name of whoever owns a key, for messages: a pad name or the name of a function.</summary>
    private string OwnerName(string owner) =>
        FunctionKeys.FirstOrDefault(f => f.OwnerId == owner)?.Name
        ?? Tiles.FirstOrDefault(t => t.OwnerId == owner)?.Name
        ?? L.Get("Key.AnotherPad");

    private IEnumerable<(string Owner, HotkeyGesture? Gesture)> AllKeys() =>
        FunctionKeys.Select(f => (f.OwnerId, HotkeyGesture.Parse(FunctionKeyToken(f.OwnerId))))
            .Concat(Tiles.Select(t => (t.OwnerId, HotkeyGesture.Parse(t.Config.Hotkey))));

    partial void OnHotkeysPausedChanged(bool value)
    {
        _hotkeys.Paused = value;
        RefreshHotkeyViews();
    }

    private void ApplyAllHotkeys()
    {
        // Function keys first: with a duplicate from a hand-edited profile, the first binding wins.
        _hotkeys.SetAll(AllKeys().ToList());
        RefreshHotkeyViews();
    }

    private void OnHotkey(string owner)
    {
        if (owner == StopAllOwner)
        {
            StopAll();
            return;
        }
        var tile = Tiles.FirstOrDefault(t => t.OwnerId == owner);
        if (tile is not null) OnTileClicked(tile);
    }

    /// <summary>Next free numpad key (Num 1 … Num 9, Num 0, then Ctrl+Alt+Num …).</summary>
    public HotkeyGesture? SuggestHotkey(TileViewModel forTile) =>
        HotkeyGesture.NumpadSuggestions().Cast<HotkeyGesture?>()
            .FirstOrDefault(g => _hotkeys.FindOwner(g!.Value, forTile.OwnerId) is null);

    private HotkeyGesture? SuggestFor(IKeyTarget target) => target switch
    {
        TileViewModel tile => SuggestHotkey(tile),
        // "Stop" goes back to its usual key, if that is free.
        FunctionKeyViewModel f when f.OwnerId == StopAllOwner && FunctionKeyToken(StopAllOwner) is null
            && _hotkeys.FindOwner(HotkeyGesture.StopAllDefault, f.OwnerId) is null => HotkeyGesture.StopAllDefault,
        _ => null,
    };

    public void BeginKeyCapture(IKeyTarget target)
    {
        if (Capturing is not null) Capturing.IsCapturingKey = false;
        Capturing = target;
        _refusedKeys.Remove(target.OwnerId);
        _captureSuggestion = SuggestFor(target);
        target.CaptureHint = _captureSuggestion is { } suggestion
            ? L.F("Key.CaptureHintSuggest", target.Name, suggestion.Display)
            : L.F("Key.CaptureHint", target.Name);
        // Registered keys never reach the window; release them while waiting for a key.
        _hotkeys.Paused = true;
        target.IsCapturingKey = true;
    }

    /// <summary>"Choose another key" for a function key from a notice: the keys tab, waiting on its row.</summary>
    private void ChooseFunctionKey(FunctionKeyViewModel f)
    {
        OpenSettings(SettingsTabs.Keys);
        BeginKeyCapture(f);
    }

    public void CancelKeyCapture()
    {
        if (Capturing is null) return;
        Capturing.IsCapturingKey = false;
        Capturing = null;
        _captureSuggestion = null;
        _hotkeys.Paused = HotkeysPaused;
        RefreshHotkeyViews();
    }

    /// <summary>
    /// A key arrived while a pad or row waits for one. Returns true if it was consumed.
    /// Enter takes the suggestion shown (nothing else), Esc cancels, Delete/Backspace removes the key.
    /// </summary>
    public bool OnCapturedKey(int vk, HotkeyModifiers mods)
    {
        var target = Capturing;
        if (target is null) return false;
        if (vk == 0x1B) // Esc
        {
            CancelKeyCapture();
            return true;
        }
        if (vk is 0x2E or 0x08) // Delete, Backspace
        {
            CancelKeyCapture();
            SetKey(target, null);
            return true;
        }
        if (vk == 0x0D) // Enter: only a suggestion that is on screen
        {
            var s = _captureSuggestion;
            if (s is null) return true; // nothing offered: keep waiting for a real key
            CancelKeyCapture();
            SetKey(target, s);
            return true;
        }
        if (!HotkeyGesture.IsUsableKey(vk)) return true; // wait for the real key (modifier alone)
        CancelKeyCapture();
        SetKey(target, new HotkeyGesture(mods, vk));
        return true;
    }

    public void SetKey(IKeyTarget target, HotkeyGesture? gesture)
    {
        if (target is TileViewModel tile) SetTileHotkey(tile, gesture);
        else if (target is FunctionKeyViewModel f) SetFunctionHotkey(f, gesture);
    }

    public void SetTileHotkey(TileViewModel tile, HotkeyGesture? gesture)
    {
        if (gesture is { } g && HotkeyRules.Conflict(g, tile.OwnerId, AllKeys(), IsFunctionOwner) is { } owner)
        {
            tile.SetKeyIssue(() => L.F("Key.IsFunctionKey", g.Display, OwnerName(owner)), T("Action.OtherKey"), () => BeginKeyCapture(tile));
            tile.IsIssueOpen = true;
            return;
        }
        tile.Config.Hotkey = gesture?.ToToken();
        _hotkeys.Set(tile.OwnerId, gesture);
        ScheduleSave();
        RefreshHotkeyViews();
    }

    private void SetFunctionHotkey(FunctionKeyViewModel f, HotkeyGesture? gesture)
    {
        if (gesture is { } g && HotkeyRules.Conflict(g, f.OwnerId, AllKeys(), IsFunctionOwner) is { } owner)
        {
            // The key stays where it is; the row says whose it is.
            _refusedKeys[f.OwnerId] = () => L.F("Keys.FunctionTaken", g.Display, OwnerName(owner));
            RefreshHotkeyViews();
            return;
        }
        _refusedKeys.Remove(f.OwnerId);
        SetFunctionKeyToken(f.OwnerId, gesture?.ToToken());
        _hotkeys.Set(f.OwnerId, gesture);
        ScheduleSave();
        RefreshHotkeyViews();
        OnPropertyChanged(nameof(StopAllToolTip));
    }

    /// <summary>Gives the key to <paramref name="tile"/> and removes it from the tile that had it.</summary>
    private void MoveHotkeyTo(TileViewModel tile, string fromOwner)
    {
        var from = Tiles.FirstOrDefault(t => t.OwnerId == fromOwner);
        if (from is not null)
        {
            from.Config.Hotkey = null;
            _hotkeys.Set(fromOwner, null);
        }
        _hotkeys.Set(tile.OwnerId, HotkeyGesture.Parse(tile.Config.Hotkey));
        ScheduleSave();
        RefreshHotkeyViews();
    }

    /// <summary>Updates the keys printed on the pads, the rows in the keys tab and the problem boxes.</summary>
    private void RefreshHotkeyViews()
    {
        int active = 0, problems = 0;
        foreach (var f in FunctionKeys) RefreshFunctionKey(f, ref problems);
        foreach (var b in _hotkeys.Bindings)
        {
            if (IsFunctionOwner(b.Owner)) continue;
            if (b.State == HotkeyState.Active) active++;
            var tile = Tiles.FirstOrDefault(t => t.OwnerId == b.Owner);
            if (tile is null) continue;
            tile.HotkeyText = b.Gesture?.Display;
            tile.HotkeyVariants = b.Gesture is { } shown ? KeyCapText.Variants(shown) : null;
            tile.HotkeyProblem = b.State is HotkeyState.Duplicate or HotkeyState.TakenByOtherProgram or HotkeyState.Failed;
            Func<string>? issue = null, action = null;
            Action? fix = null;
            var g = b.Gesture;
            switch (b.State)
            {
                case HotkeyState.Duplicate:
                    problems++;
                    var dupOf = b.DuplicateOf!;
                    var firstName = OwnerName(dupOf);
                    issue = () => L.F("Key.Duplicate", g?.Display, firstName);
                    if (IsFunctionOwner(dupOf))
                    {
                        action = T("Action.OtherKey");
                        fix = () => BeginKeyCapture(tile);
                    }
                    else
                    {
                        action = () => L.F("Key.MoveHere", g?.Display);
                        fix = () => MoveHotkeyTo(tile, dupOf);
                    }
                    tile.HotkeyStateText = L.F("Key.StateDuplicate", firstName);
                    break;
                case HotkeyState.TakenByOtherProgram:
                    problems++;
                    issue = () => L.F("Key.TakenByOther", g?.Display);
                    action = T("Action.OtherKey");
                    fix = () => BeginKeyCapture(tile);
                    tile.HotkeyStateText = L.Get("Key.StateTaken");
                    break;
                case HotkeyState.Failed:
                    problems++;
                    issue = () => L.F("Key.Refused", g?.Display);
                    action = T("Action.OtherKey");
                    fix = () => BeginKeyCapture(tile);
                    tile.HotkeyStateText = L.Get("Key.StateRefused");
                    break;
                default:
                    tile.HotkeyStateText = L.Get(b.Gesture is null ? "Key.StateNone"
                        : HotkeysPaused ? "Key.StatePaused"
                        : "Key.StateWorks");
                    break;
            }
            tile.SetKeyIssue(issue, action, fix);
        }
        foreach (var t in Tiles.Where(t => t.HotkeyText is null && !_hotkeys.Bindings.Any(b => b.Owner == t.OwnerId)))
            t.HotkeyStateText = L.Get("Key.StateNone");

        HotkeyProblems = problems;
        StatusHotkeys = HotkeysPaused ? L.Get("Status.KeysPaused")
            : problems > 0 ? L.F("Status.KeysProblems", active, problems)
            : active == 0 ? L.Get("Status.KeysNone")
            : L.Count("Status.Keys", active);
    }

    /// <summary>State of one function key row; a key held by another program also gets a notice with the way out.</summary>
    private void RefreshFunctionKey(FunctionKeyViewModel f, ref int problems)
    {
        var b = _hotkeys.Get(f.OwnerId);
        var g = b?.Gesture;
        f.HotkeyText = g?.Display;
        string noticeKey = "hotkey-" + f.OwnerId;
        if (b is { State: HotkeyState.TakenByOtherProgram or HotkeyState.Failed })
        {
            problems++;
            f.SetProblem(L.F(b.State == HotkeyState.Failed ? "Key.Refused" : "Key.TakenByOther", g?.Display), L.Get("Action.OtherKey"), () => BeginKeyCapture(f));
            if (f.OwnerId == StopAllOwner)
                ShowNotice(noticeKey, () => L.F("Notice.StopKeyTaken", g?.Display), T("Action.OtherKey"), () => ChooseFunctionKey(f));
        }
        else
        {
            ClearNotice(noticeKey);
            f.SetProblem(null, null, null);
            f.HotkeyStateText = L.Get(g is null ? "Key.StateNone" : HotkeysPaused ? "Key.StatePaused" : "Key.StateWorks");
        }
        if (_refusedKeys.TryGetValue(f.OwnerId, out var refused))
            f.SetProblem(refused(), L.Get("Action.OtherKey"), () => BeginKeyCapture(f));
    }

    /// <summary>Number of keys that do not work (status line warns when > 0).</summary>
    [ObservableProperty]
    private int _hotkeyProblems;

    // ---------------------------------------------------------------- saving

    public void ScheduleSave()
    {
        if (_saveTimer is null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow() => TrySave();

    private bool TrySave()
    {
        _saveTimer.Stop();
        try
        {
            ProfileStore.Save(_paths.ProfileFile, _profile);
            ClearNotice("save");
            _lastSaveOk = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("save", TF("Notice.SaveFailed", ex.Message), T("Action.Retry"), SaveNow);
            _lastSaveOk = false;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        L.Changed -= OnLanguageChanged;
        _progressTimer.Stop();
        _deviceTimer.Stop();
        // Pictures of removed pads (or replaced pictures) go once the profile without them is on disk.
        bool saved = _saveTimer.IsEnabled ? TrySave() : _lastSaveOk;
        if (saved) RemoveReleasedImages();
        StopMix();
        StopSink();
        _hotkeys.Dispose();
        if (_services.UseDirtyFlag) _dirty.Delete();
        _services.Hotkeys.Dispose();
        _audio.Dispose();
    }
}
