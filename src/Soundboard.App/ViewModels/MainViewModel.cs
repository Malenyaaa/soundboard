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

namespace Soundboard.App.ViewModels;

/// <summary>
/// Main window logic: device list (read only), tiles, monitor output. Windows audio settings
/// are only read, never changed; the monitor device is opened by ID in shared mode.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly string[] Palette =
    [
        "#DCEBFF", "#FFE3C2", "#D9F2D9", "#F6D6E8", "#E6DDF7", "#FFF2B3", "#D5F0F0", "#F2DCCF",
    ];

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

    public MainViewModel(AppServices services)
    {
        _services = services;
        _paths = services.Paths;
        _audio = services.Audio;
        _dirty = new DirtyFlag(_paths.DirtyFlagFile);
        _dispatcher = Dispatcher.CurrentDispatcher;

        var loaded = ProfileStore.Load(_paths.ProfileFile);
        _profile = loaded.Profile;
        if (loaded.Problem is not null) ShowNotice("profile", loaded.Problem);

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
            if (def?.Id is not null)
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
        }

        _hotkeys = new HotkeyService(services.Hotkeys);
        services.Hotkeys.Pressed += id => _hotkeys.HandleHotkey(id);
        _hotkeys.Triggered += (_, owner) => OnHotkey(owner);
        ApplyAllHotkeys();

        ThemeManager.Changed += (_, _) => { foreach (var t in Tiles) t.RefreshTheme(); };

        InitAudioState();

        _initialized = true;
        EnsureMonitor();
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
    private double _masterVolumePercent;

    [ObservableProperty]
    private bool _overlayEnabled;

    [ObservableProperty]
    private bool _showNotPresent;

    [ObservableProperty]
    private string _devicesHeader = "";

    [ObservableProperty]
    private string _statusDefaults = "";

    [ObservableProperty]
    private string _statusMonitor = "";

    // ---------------------------------------------------------------- notices

    /// <summary>Shows a yellow notice; a notice with the same key is replaced.</summary>
    public void ShowNotice(string key, string text, string? actionText = null, Action? action = null)
    {
        var existing = Notices.FirstOrDefault(n => n.Key == key);
        var notice = new NoticeViewModel(key, text, n => Notices.Remove(n), actionText, action);
        if (existing is not null) Notices[Notices.IndexOf(existing)] = notice;
        else Notices.Add(notice);
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
    }

    private void RefreshDeviceViews()
    {
        Devices.Clear();
        foreach (var d in _state.Devices.Where(d => ShowNotPresent || d.Status != DeviceStatus.NotPresent))
            Devices.Add(DeviceRowViewModel.From(d));
        int render = _state.Devices.Count(d => d.Flow == DeviceFlow.Render && d.Status == DeviceStatus.Active);
        int capture = _state.Devices.Count(d => d.Flow == DeviceFlow.Capture && d.Status == DeviceStatus.Active);
        DevicesHeader = $"Audiogeräte in Windows (nur angezeigt, hier wird nichts verändert) – aktiv: {render} Wiedergabe, {capture} Aufnahme";
        NotPresentCount = _state.Devices.Count(d => d.Status == DeviceStatus.NotPresent);

        var defaultRender = _state.GetDefault(DeviceFlow.Render, DeviceRoleKind.Multimedia);
        _syncingSelection = true;
        try
        {
            OutputChoices.Clear();
            foreach (var d in _state.Devices.Where(d => d.Flow == DeviceFlow.Render && d.Status == DeviceStatus.Active))
                OutputChoices.Add(new OutputChoice(d.Id, d.Name, true, d.Id == defaultRender?.Id));

            var id = _profile.MonitorDeviceId;
            if (id is not null && OutputChoices.All(c => c.Id != id))
            {
                var known = _state.FindById(id);
                OutputChoices.Add(new OutputChoice(id, known?.Name ?? _profile.MonitorDeviceName ?? "Gewähltes Gerät", false, false));
            }
            SelectedOutput = OutputChoices.FirstOrDefault(c => c.Id == id);
        }
        finally
        {
            _syncingSelection = false;
        }

        RefreshMicChoices();

        StatusDefaults = $"Standard-Wiedergabe: {DefaultText(DeviceFlow.Render)}   ·   Standard-Aufnahme: {DefaultText(DeviceFlow.Capture)}";
    }

    private string DefaultText(DeviceFlow flow)
    {
        var main = _state.GetDefault(flow, DeviceRoleKind.Multimedia);
        var comm = _state.GetDefault(flow, DeviceRoleKind.Communications);
        var text = main?.Name ?? "keins";
        if (comm?.Id is not null && comm.Id != main?.Id) text += $" (Kommunikation: {comm.Name})";
        return text;
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
            ShowNotice("monitor", "Es ist kein Gerät zum Mithören gewählt, und Windows meldet kein Standard-Wiedergabegerät. Schließe Kopfhörer oder Lautsprecher an und wähle sie oben bei „Mithören auf“.");
            UpdateMonitorStatus();
            return;
        }

        IAudioOutput? sink = null;
        try
        {
            sink = _audio.OpenOutput(id, 30);
            if (sink is null)
            {
                var name = _profile.MonitorDeviceName ?? "Das gewählte Gerät";
                ShowNotice("monitor", $"Das Mithörgerät „{name}“ ist nicht angeschlossen oder in Windows deaktiviert. Schließe es an – das Mithören startet dann von selbst – oder wähle oben bei „Mithören auf“ ein anderes Gerät.");
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
            ShowNotice("monitor", ex.Message + " Wähle oben ein anderes Gerät oder schließe das andere Programm.", "Erneut versuchen", EnsureMonitor);
        }
        catch (COMException ex)
        {
            sink?.Dispose();
            ShowNotice("monitor", $"Das Mithörgerät antwortet nicht ({ex.Message}). Wähle oben ein anderes Gerät oder versuche es erneut.", "Erneut versuchen", EnsureMonitor);
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
        var name = _profile.MonitorDeviceName ?? "das Mithörgerät";
        var why = error is null ? "" : $" ({error.Message})";
        ShowNotice("monitor", $"Die Wiedergabe auf „{name}“ wurde unterbrochen{why}. Wurde das Gerät abgezogen? Sobald es wieder da ist, startet das Mithören von selbst.", "Erneut versuchen", EnsureMonitor);
        UpdateMonitorStatus();
    }

    /// <summary>The monitor device has another mix format: decode all sounds again for it.</summary>
    private void SwitchFormat(TargetFormat format)
    {
        StopMix("Die Sounds werden gerade neu geladen, weil das Mithörgerät ein anderes Format hat.");
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
        var name = SelectedOutput?.Name ?? _profile.MonitorDeviceName ?? "kein Gerät";
        StatusMonitor = !MonitorEnabled ? "Mithören: aus"
            : _sink is null ? $"Mithören: gestört ({name})"
            : $"Mithören: an – {name} ({_player.Format.SampleRate:N0} Hz, {_player.Format.Channels} Kanäle)";
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
            _player.SetVolume(clip.Handle, tile.Volume);
        }
        catch (SoundLoadException ex)
        {
            if (version != tile.LoadVersion) return;
            tile.Clip = null;
            tile.ErrorText = ex.Message;
            tile.State = TileState.Error;
            ShowNotice("load-" + tile.Config.Id, $"Kachel „{tile.Name}“: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version != tile.LoadVersion) return;
            tile.Clip = null;
            tile.ErrorText = $"Die Datei ließ sich nicht laden ({ex.Message}).";
            tile.State = TileState.Error;
            ShowNotice("load-" + tile.Config.Id, $"Kachel „{tile.Name}“: {tile.ErrorText}");
        }
        tile.RefreshStatus();
    }

    public void OnTileClicked(TileViewModel tile)
    {
        if (tile.IsRenaming) return;
        switch (tile.State)
        {
            case TileState.Loading:
                ShowNotice("play", $"„{tile.Name}“ wird gerade noch geladen. Gleich noch einmal klicken.");
                return;
            case TileState.Error:
                ShowNotice("play", $"„{tile.Name}“ lässt sich nicht abspielen: {tile.ErrorText} Rechtsklick auf die Kachel → „Entfernen“, dann die Datei neu hereinziehen.");
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
            ShowNotice("play", MonitorEnabled
                ? "Der Sound geht gerade nirgendwohin: Das Mithörgerät funktioniert nicht, und es wird nichts ans Kabel gesendet. Wähle oben bei „Mithören auf“ ein angeschlossenes Gerät."
                : "Der Sound geht gerade nirgendwohin: Mithören ist aus, und es wird nichts ans Kabel gesendet. Setze oben das Häkchen bei „Mithören auf“.");
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

        var rejected = new List<string>();
        var failed = new List<string>();
        int added = 0;
        foreach (var file in files)
        {
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
                Color = Palette[_profile.Tiles.Count % Palette.Length],
            };
            _profile.Tiles.Add(config);
            var tile = new TileViewModel(this, config);
            Tiles.Add(tile);
            _ = LoadTileAsync(tile);
            added++;
        }

        if (added > 0) ScheduleSave();
        if (rejected.Count > 0)
            ShowNotice("rejected", $"Nicht übernommen: {string.Join(", ", rejected)}. Möglich sind MP3, WAV, OGG und FLAC.");
        else
            ClearNotice("rejected");
        if (failed.Count > 0)
            ShowNotice("copy", $"Konnte nicht in den Sound-Ordner kopiert werden: {string.Join(", ", failed)}");
    }

    [RelayCommand]
    private void AddFilesDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Sounds hinzufügen",
            Multiselect = true,
            Filter = "Sounds (MP3, WAV, OGG, FLAC)|*.mp3;*.wav;*.ogg;*.flac|Alle Dateien|*.*",
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
        ClearNotice("load-" + tile.Config.Id);
        _hotkeys.Remove(tile.Config.Id.ToString());
        ClearNotice("hotkey-" + tile.Config.Id);
        RefreshHotkeyViews();
        ScheduleSave();

        ShowNotice("removed", $"Kachel „{tile.Name}“ entfernt. Die Datei bleibt im Sound-Ordner.", "Rückgängig", () =>
        {
            _profile.Tiles.Insert(Math.Clamp(configIndex, 0, _profile.Tiles.Count), tile.Config);
            var restored = new TileViewModel(this, tile.Config);
            Tiles.Insert(Math.Clamp(index, 0, Tiles.Count), restored);
            _ = LoadTileAsync(restored);
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

    private static readonly System.Globalization.CultureInfo De = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

    private AudioSnapshot? _snapshot;
    private bool _stateReady;

    public ObservableCollection<ComparisonRow> ComparisonRows { get; } = [];

    [ObservableProperty]
    private string _snapshotHeader = "Audio-Zustand";

    [ObservableProperty]
    private string _snapshotInfo = "";

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
            ShowNotice("snapshot-file", ex.Message + " Speichere den jetzigen Zustand neu, wenn er stimmt.", "Audio-Zustand jetzt sichern", SaveSnapshotNow);
        }

        if (_snapshot is null && !File.Exists(_paths.SnapshotFile))
        {
            // First start: remember how Windows audio looks before anything else happens.
            TrySaveSnapshot(_state);
        }

        _stateReady = true;
        CompareNow();

        if (crashed)
        {
            var tail = Comparison is { DefaultsChanged: true }
                ? "Seitdem hat sich ein Windows-Standardgerät geändert – „Wie vorher“ stellt es zurück."
                : "Die Windows-Standardgeräte sind wie gesichert; es ist nichts zu tun.";
            ShowNotice("crash", "Das Soundboard wurde beim letzten Mal nicht normal beendet. " + tail,
                Comparison is { DefaultsChanged: true } ? "Wie vorher" : null,
                Comparison is { DefaultsChanged: true } ? RestoreDefaults : null);
        }
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
            ShowNotice("snapshot-file", $"Der Audio-Zustand ließ sich nicht sichern ({ex.Message}). Ist der Datenordner schreibgeschützt?", "Erneut versuchen", SaveSnapshotNow);
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
            SnapshotHeader = "Audio-Zustand: noch nicht gesichert";
            SnapshotInfo = "Sichere den Zustand, solange alles so ist, wie du es willst. Danach siehst du hier jede Änderung.";
            ClearNotice("snapshot");
            return;
        }

        var cmp = SnapshotService.Compare(_snapshot, _state);
        Comparison = cmp;
        foreach (var r in cmp.Rows) ComparisonRows.Add(r);
        string when = _snapshot.TakenAt.ToString("dd.MM.yyyy, HH:mm", De);
        SnapshotHeader = cmp.IsIdentical
            ? $"Audio-Zustand: wie gesichert am {when}"
            : cmp.DifferenceCount == 1
                ? $"Audio-Zustand: 1 Abweichung seit der Sicherung am {when}"
                : $"Audio-Zustand: {cmp.DifferenceCount} Abweichungen seit der Sicherung am {when}";
        SnapshotInfo = cmp.UnchangedDevices == 1 ? "Ein weiteres Gerät ist unverändert." : $"{cmp.UnchangedDevices} weitere Geräte sind unverändert.";

        if (cmp.DefaultsChanged)
        {
            var lines = string.Join("; ", cmp.DefaultChangeLines);
            var lead = cmp.CableBecameDefault
                ? "Das virtuelle Kabel ist jetzt ein Windows-Standardgerät – das hat das Soundboard nicht getan (meist war es das Treiber-Setup). "
                : "Ein Windows-Standardgerät hat sich seit der Sicherung geändert – das Soundboard hat das nicht getan. ";
            ShowNotice("snapshot", lead + lines + ". Wolltest du das selbst so, sichere unten unter „Audio-Zustand“ neu.", "Wie vorher", RestoreDefaults);
        }
        else
        {
            ClearNotice("snapshot");
        }
    }

    /// <summary>Puts the default devices back to the saved ones (and only those).</summary>
    [RelayCommand]
    private void RestoreDefaults()
    {
        if (_snapshot is null)
        {
            ShowNotice("restore", "Es gibt noch keine Sicherung, auf die sich zurückstellen ließe. Sichere zuerst den Audio-Zustand.", "Audio-Zustand jetzt sichern", SaveSnapshotNow);
            return;
        }

        var before = _audio.ReadState();
        var cmp = SnapshotService.Compare(_snapshot, before);
        if (!cmp.DefaultsChanged)
        {
            RestoreResultText = "Die Standardgeräte sind schon wie gesichert – es war nichts zu tun.";
            RefreshDevices();
            return;
        }

        var result = SnapshotService.Restore(_snapshot, before, _services.Policy);
        RefreshDevices();
        RestoreResultText = result.RestoredCount switch
        {
            0 => "Es wurde nichts zurückgestellt.",
            1 => "1 Standardgerät zurückgestellt. Prüfe es zur Sicherheit in den Windows-Soundeinstellungen.",
            _ => $"{result.RestoredCount} Standardgeräte zurückgestellt. Prüfe sie zur Sicherheit in den Windows-Soundeinstellungen.",
        };
        if (result.AllGood) ClearNotice("restore");
        else
            ShowNotice("restore", "Nicht alles ließ sich automatisch zurückstellen. So geht es von Hand: " + result.ManualSteps().Replace(Environment.NewLine, "; ").Replace("\n", "; "));
    }

    /// <summary>Saves the current Windows audio state as the new reference.</summary>
    [RelayCommand]
    private void SaveSnapshotNow()
    {
        RefreshDevices();
        if (TrySaveSnapshot(_state))
        {
            RestoreResultText = $"Gesichert am {DateTime.Now.ToString("dd.MM.yyyy, HH:mm", De)}. Die vorige Sicherung liegt als „audio-snapshot.vorher.json“ im Datenordner.";
            CompareNow();
        }
    }

    [RelayCommand]
    private void CompareAgain()
    {
        RefreshDevices();
        RestoreResultText = Comparison is null ? "" : Comparison.IsIdentical ? "Verglichen: alles wie gesichert." : "Verglichen: die Abweichungen stehen in der Liste.";
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
        [.. Enum.GetValues<BufferSize>().Select(b => new BufferChoice(b, BufferSizes.Display(b)))];

    [ObservableProperty]
    private OutputChoice? _selectedMic;

    [ObservableProperty]
    private BufferChoice? _selectedBuffer;

    [ObservableProperty]
    private bool _blockVoice;

    [ObservableProperty]
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
                MicChoices.Add(new OutputChoice(id, _state.FindById(id)?.Name ?? _profile.MicDeviceName ?? "Gewähltes Mikrofon", false, false));
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

    /// <summary>Opens the chosen microphone. Null plus a yellow notice if that is not possible.</summary>
    private ICaptureInput? OpenMic(string noticeKey)
    {
        var id = _profile.MicDeviceId;
        if (id is null)
        {
            ShowNotice(noticeKey, "Es ist kein Mikrofon gewählt, und Windows meldet keins. Schließe ein Mikrofon an und wähle es oben bei „Mikrofon“.");
            return null;
        }
        try
        {
            var mic = _audio.OpenCapture(id, BufferSizes.Milliseconds(_profile.Buffer));
            if (mic is null)
            {
                var name = _profile.MicDeviceName ?? "Das gewählte Mikrofon";
                ShowNotice(noticeKey, $"Das Mikrofon „{name}“ ist nicht angeschlossen oder in Windows deaktiviert. Schließe es an oder wähle oben bei „Mikrofon“ ein anderes.");
            }
            return mic;
        }
        catch (SinkException ex)
        {
            ShowNotice(noticeKey, ex.Message + " Wähle oben bei „Mikrofon“ ein anderes oder versuche es erneut.");
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
            _player.SetMixActive(false);
            if (session is not null) session.Dispose();
            else
            {
                output.Dispose();
                mic?.Dispose();
            }
            ShowNotice(noticeKey, ex is SinkException ? ex.Message : $"Die Ausgabe ließ sich nicht starten ({ex.Message}).", "Erneut versuchen", purpose == MixPurpose.FileTest ? RecordTest : RestartMix);
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
        if (purpose == MixPurpose.FileTest && reason is not null)
        {
            TestStatus = "";
            ShowNotice("test", reason + " Die Test-Aufnahme wurde abgebrochen; starte sie neu.", "Neu aufnehmen", RecordTest);
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
            TestStatus = $"Fertig: {Path.GetFileName(file)} – zum Anhören im Ordner öffnen.";
            HasTestFile = true;
            ClearNotice("test");
            RestartMix();
            return;
        }

        var why = e.Error is null ? "" : $" ({e.Error.Message})";
        if (e.Microphone)
        {
            var name = _profile.MicDeviceName ?? "Das Mikrofon";
            StopMix($"„{name}“ hat aufgehört zu liefern{why}. Wurde es abgezogen?");
            if (purpose == MixPurpose.Cable)
                ShowNotice("cable", $"„{name}“ hat aufgehört zu liefern{why}. Wurde es abgezogen? Sobald es wieder da ist, startet die Übertragung von selbst.", "Erneut versuchen", RestartMix);
        }
        else
        {
            StopMix($"Die Ausgabe wurde unterbrochen{why}.");
            if (purpose == MixPurpose.Cable)
                ShowNotice("cable", $"Die Übertragung ans Kabel wurde unterbrochen{why}. Sobald das Kabel wieder da ist, startet sie von selbst.", "Erneut versuchen", RestartMix);
        }
    }

    /// <summary>Restarts the permanent mix output (if one is configured) after a change.</summary>
    private void RestartMix()
    {
    }

    /// <summary>Records microphone + sounds for 10 seconds into a WAV file (test without cable).</summary>
    [RelayCommand]
    private void RecordTest()
    {
        if (_mixPurpose == MixPurpose.FileTest && _fileSink is not null)
        {
            int left = (int)Math.Ceiling(TestLength.TotalSeconds * (1 - _fileSink.Progress));
            ShowNotice("test", $"Die Test-Aufnahme läuft schon – noch {left} s.");
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
            TestStatus = "Aufnahme läuft – sprich und spiel Sounds ab. Noch 10 s.";
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
            TestStatus = $"Aufnahme läuft – sprich und spiel Sounds ab. Noch {left} s.";
        }

        var micName = SelectedMic?.Name ?? _profile.MicDeviceName ?? "keins";
        string text = mix is null || !mix.HasVoice
            ? "Mikrofon: aus"
            : mix.Underruns == 0 ? $"Mikrofon: {micName}, keine Aussetzer"
            : $"Mikrofon: {micName}, Aussetzer: {mix.Underruns}";
        if (text != StatusMix) StatusMix = text;
    }

    // ---------------------------------------------------------------- hotkeys

    private const string StopAllOwner = "stop-all";

    [ObservableProperty]
    private bool _hotkeysPaused;

    [ObservableProperty]
    private string _statusHotkeys = "";

    /// <summary>Tile currently waiting for a key press, or null.</summary>
    public TileViewModel? CapturingTile { get; private set; }

    public string StopAllToolTip => HotkeyGesture.Parse(_profile.StopAllHotkey) is { } g
        ? $"Stoppt alle laufenden Sounds sofort. Taste: {g.Display} – wirkt überall, auch wenn ein Spiel im Vordergrund ist."
        : "Stoppt alle laufenden Sounds sofort.";

    partial void OnHotkeysPausedChanged(bool value)
    {
        _hotkeys.Paused = value;
        RefreshHotkeyViews();
    }

    private void ApplyAllHotkeys()
    {
        var list = new List<(string, HotkeyGesture?)> { (StopAllOwner, HotkeyGesture.Parse(_profile.StopAllHotkey)) };
        foreach (var t in Tiles) list.Add((t.Config.Id.ToString(), HotkeyGesture.Parse(t.Config.Hotkey)));
        _hotkeys.SetAll(list);
        RefreshHotkeyViews();
    }

    private void OnHotkey(string owner)
    {
        if (owner == StopAllOwner)
        {
            StopAll();
            return;
        }
        var tile = Tiles.FirstOrDefault(t => t.Config.Id.ToString() == owner);
        if (tile is not null) OnTileClicked(tile);
    }

    /// <summary>Next free numpad key (Num 1 … Num 9, Num 0, then Strg+Alt+Num …).</summary>
    public HotkeyGesture? SuggestHotkey(TileViewModel forTile) =>
        HotkeyGesture.NumpadSuggestions().Cast<HotkeyGesture?>()
            .FirstOrDefault(g => _hotkeys.FindOwner(g!.Value, forTile.Config.Id.ToString()) is null);

    public void BeginKeyCapture(TileViewModel tile)
    {
        if (CapturingTile is not null) CapturingTile.IsCapturingKey = false;
        CapturingTile = tile;
        var suggestion = SuggestHotkey(tile);
        tile.CaptureHint = suggestion is null
            ? "Taste drücken\nEsc: abbrechen\nEntf: Taste löschen"
            : $"Taste drücken\nEnter: {suggestion.Value.Display} nehmen\nEsc: abbrechen\nEntf: Taste löschen";
        // Registered keys never reach the window; release them while waiting for a key.
        _hotkeys.Paused = true;
        tile.IsCapturingKey = true;
    }

    public void CancelKeyCapture()
    {
        if (CapturingTile is null) return;
        CapturingTile.IsCapturingKey = false;
        CapturingTile = null;
        _hotkeys.Paused = HotkeysPaused;
        RefreshHotkeyViews();
    }

    /// <summary>
    /// A key arrived while a tile waits for one. Returns true if it was consumed.
    /// Enter takes the suggestion, Esc cancels, Delete/Backspace removes the key.
    /// </summary>
    public bool OnCapturedKey(int vk, HotkeyModifiers mods)
    {
        var tile = CapturingTile;
        if (tile is null) return false;
        if (vk == 0x1B) // Esc
        {
            CancelKeyCapture();
            return true;
        }
        if (vk is 0x2E or 0x08) // Delete, Backspace
        {
            CancelKeyCapture();
            SetTileHotkey(tile, null);
            return true;
        }
        if (vk == 0x0D) // Enter: take the suggestion
        {
            var s = SuggestHotkey(tile);
            CancelKeyCapture();
            if (s is not null) SetTileHotkey(tile, s);
            return true;
        }
        if (!HotkeyGesture.IsUsableKey(vk)) return true; // wait for the real key (modifier alone)
        CancelKeyCapture();
        SetTileHotkey(tile, new HotkeyGesture(mods, vk));
        return true;
    }

    public void SetTileHotkey(TileViewModel tile, HotkeyGesture? gesture)
    {
        if (gesture is { } g && g == HotkeyGesture.Parse(_profile.StopAllHotkey))
        {
            ShowNotice("hotkey-" + tile.Config.Id, $"{g.Display} ist schon „Alles stoppen“. Wähle für „{tile.Name}“ eine andere Taste (Rechtsklick → „Taste belegen …“).",
                "Andere Taste wählen", () => BeginKeyCapture(tile));
            return;
        }
        tile.Config.Hotkey = gesture?.ToToken();
        _hotkeys.Set(tile.Config.Id.ToString(), gesture);
        ScheduleSave();
        RefreshHotkeyViews();
    }

    /// <summary>Gives the key to <paramref name="tile"/> and removes it from the tile that had it.</summary>
    private void MoveHotkeyTo(TileViewModel tile, string fromOwner)
    {
        var from = Tiles.FirstOrDefault(t => t.Config.Id.ToString() == fromOwner);
        if (from is not null)
        {
            from.Config.Hotkey = null;
            _hotkeys.Set(fromOwner, null);
        }
        _hotkeys.Set(tile.Config.Id.ToString(), HotkeyGesture.Parse(tile.Config.Hotkey));
        ScheduleSave();
        RefreshHotkeyViews();
    }

    /// <summary>Updates tile labels and the yellow notices for conflicts.</summary>
    private void RefreshHotkeyViews()
    {
        int active = 0, problems = 0;
        foreach (var b in _hotkeys.Bindings)
        {
            if (b.State == HotkeyState.Active) active++;
            string key = "hotkey-" + b.Owner;
            if (b.Owner == StopAllOwner)
            {
                if (b.State is HotkeyState.TakenByOtherProgram or HotkeyState.Failed)
                {
                    problems++;
                    ShowNotice(key, $"Die Taste {b.Gesture?.Display} für „Alles stoppen“ ist schon von einem anderen Programm belegt. Der Knopf „Alles stoppen“ funktioniert weiter; die Taste nicht, solange das andere Programm läuft.");
                }
                else ClearNotice(key);
                continue;
            }

            var tile = Tiles.FirstOrDefault(t => t.Config.Id.ToString() == b.Owner);
            if (tile is null) continue;
            tile.HotkeyText = b.Gesture?.Display;
            tile.HotkeyProblem = b.State is HotkeyState.Duplicate or HotkeyState.TakenByOtherProgram or HotkeyState.Failed;
            switch (b.State)
            {
                case HotkeyState.Duplicate:
                    problems++;
                    var first = Tiles.FirstOrDefault(t => t.Config.Id.ToString() == b.DuplicateOf);
                    var firstName = first?.Name ?? "eine andere Kachel";
                    ShowNotice(key, $"{b.Gesture?.Display} gehört schon zu „{firstName}“. „{tile.Name}“ reagiert deshalb nicht auf diese Taste.",
                        $"{b.Gesture?.Display} zu „{tile.Name}“ verschieben", () => MoveHotkeyTo(tile, b.DuplicateOf!));
                    break;
                case HotkeyState.TakenByOtherProgram:
                    problems++;
                    ShowNotice(key, $"{b.Gesture?.Display} ist schon von einem anderen Programm belegt. „{tile.Name}“ reagiert nicht darauf. Wähle eine andere Taste oder beende das andere Programm.",
                        "Andere Taste wählen", () => BeginKeyCapture(tile));
                    break;
                case HotkeyState.Failed:
                    problems++;
                    ShowNotice(key, $"Windows hat die Taste {b.Gesture?.Display} für „{tile.Name}“ nicht angenommen. Wähle eine andere Taste.",
                        "Andere Taste wählen", () => BeginKeyCapture(tile));
                    break;
                default:
                    ClearNotice(key);
                    break;
            }
        }

        StatusHotkeys = HotkeysPaused ? "Hotkeys: pausiert"
            : problems > 0 ? $"Hotkeys: {active} aktiv, {problems} gestört"
            : $"Hotkeys: {active} aktiv";
    }

    // ---------------------------------------------------------------- saving

    public void ScheduleSave()
    {
        if (_saveTimer is null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            ProfileStore.Save(_paths.ProfileFile, _profile);
            ClearNotice("save");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("save", $"Die Einstellungen ließen sich nicht speichern ({ex.Message}). Ist der Datenordner schreibgeschützt?", "Erneut versuchen", SaveNow);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _progressTimer.Stop();
        _deviceTimer.Stop();
        if (_saveTimer.IsEnabled) SaveNow();
        StopMix();
        StopSink();
        _hotkeys.Dispose();
        if (_services.UseDirtyFlag) _dirty.Delete();
        _services.Hotkeys.Dispose();
        _audio.Dispose();
    }
}
