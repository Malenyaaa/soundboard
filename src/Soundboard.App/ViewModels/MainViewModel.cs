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
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _deviceTimer;
    private readonly SoundBank _bank;
    private SoundPlayer _player;
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

    private SoundPlayer CreatePlayer(TargetFormat format) => new(format)
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
            _player.Reset();
            sink.Stopped += OnSinkStopped;
            sink.Start(_player);
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
        _player?.Reset();
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
        _player = CreatePlayer(format);
        _bank.Reset(format);
        foreach (var tile in Tiles)
        {
            tile.Clip = null;
            _ = LoadTileAsync(tile);
        }
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

        if (!MonitorEnabled)
        {
            ShowNotice("play", "Mithören ist ausgeschaltet – deshalb ist nichts zu hören. Setze oben das Häkchen bei „Mithören auf“.");
            return;
        }
        if (_sink is null)
        {
            ShowNotice("play", "Es gibt gerade kein funktionierendes Gerät zum Mithören. Wähle oben bei „Mithören auf“ ein angeschlossenes Gerät.");
            return;
        }

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
        var now = DateTime.UtcNow;
        foreach (var t in Tiles)
        {
            if (t.Clip is null)
            {
                t.IsPlaying = false;
                continue;
            }
            var p = _sink is null ? null : _player.GetProgress(t.Clip.Handle);
            if (p is null && (now - t.PlayRequestedAt).TotalMilliseconds < 250 && _sink is not null)
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
            ? "Taste drücken … Esc: abbrechen, Entf: Taste entfernen"
            : $"Taste drücken … Enter: {suggestion.Value.Display} nehmen, Esc: abbrechen, Entf: entfernen";
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
        StopSink();
        _hotkeys.Dispose();
        _services.Hotkeys.Dispose();
        _audio.Dispose();
    }
}
