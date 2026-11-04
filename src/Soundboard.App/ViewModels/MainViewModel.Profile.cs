using System.IO;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Soundboard.Core.Audio;
using Soundboard.Core.Config;
using Soundboard.Core.Hotkeys;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>Profile as a ZIP file (export, import with preview) and evening out the pad volumes.</summary>
public sealed partial class MainViewModel
{

    /// <summary>Quiet line under the profile buttons: what was just saved or brought in.</summary>
    [ObservableProperty]
    private string _profileResultText = "";

    /// <summary>Why a file could not be used; shown as a notice field under the buttons.</summary>
    [ObservableProperty]
    private string _profileErrorText = "";

    /// <summary>The archive chosen for import, waiting for "Replace" or "Add to existing".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportPreview), nameof(ImportPreviewText))]
    private ArchivePreview? _importPreview;

    [ObservableProperty]
    private bool _evenOutOnImport = true;

    private string? _importPath;

    public bool HasImportPreview => ImportPreview is not null;

    public string ImportPreviewText => ImportPreview is not { } p ? ""
        : p.MissingSounds == 0 ? L.Count("Profile.Preview", p.PadCount, p.FileName)
        : L.Count("Profile.Preview", p.PadCount, p.FileName) + " " + L.Count("Profile.PreviewMissing", p.MissingSounds);

    [RelayCommand]
    private void ExportProfile()
    {
        var dlg = new SaveFileDialog
        {
            Title = L.Get("Profile.ExportTitle"),
            Filter = L.Get("Profile.Filter"),
            FileName = $"Soundboard-{DateTime.Now:yyyy-MM-dd}.zip",
            AddExtension = true,
            DefaultExt = ".zip",
        };
        if (dlg.ShowDialog() != true) return;
        ExportTo(dlg.FileName);
    }

    public void ExportTo(string path)
    {
        ProfileErrorText = "";
        try
        {
            ProfileArchive.Export(_profile, _paths, path);
            ProfileResultText = L.Count("Profile.Exported", _profile.Tiles.Count, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProfileResultText = "";
            ProfileErrorText = L.F("Profile.ExportFailed", Path.GetFileName(path), ex.Message);
        }
    }

    [RelayCommand]
    private void ImportProfile()
    {
        var dlg = new OpenFileDialog { Title = L.Get("Profile.ImportTitle"), Filter = L.Get("Profile.Filter") };
        if (dlg.ShowDialog() != true) return;
        PreviewImport(dlg.FileName);
    }

    /// <summary>Reads the archive and shows what it holds; nothing changes yet.</summary>
    public void PreviewImport(string path)
    {
        ProfileResultText = "";
        try
        {
            var (_, preview) = ProfileArchive.Read(path);
            if (preview.PadCount == 0) throw new ProfileArchiveException(L.F("Archive.NoPads", preview.FileName));
            _importPath = path;
            ImportPreview = preview;
            ProfileErrorText = "";
        }
        catch (ProfileArchiveException ex)
        {
            _importPath = null;
            ImportPreview = null;
            ProfileErrorText = ex.Message;
        }
    }

    [RelayCommand]
    private void CancelImport()
    {
        _importPath = null;
        ImportPreview = null;
    }

    [RelayCommand]
    private Task ImportReplace() => ImportAsync(replace: true);

    [RelayCommand]
    private Task ImportAdd() => ImportAsync(replace: false);

    private async Task ImportAsync(bool replace)
    {
        var path = _importPath;
        if (path is null || ImportPreview is null) return;
        ImportPreview = null;
        _importPath = null;

        string backup;
        List<TileConfig> imported;
        try
        {
            backup = BackupProfile();
            imported = ProfileArchive.Import(path, _paths);
        }
        catch (ProfileArchiveException ex)
        {
            ProfileErrorText = ex.Message;
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProfileErrorText = L.F("Profile.ImportFailed", Path.GetFileName(path), ex.Message);
            return;
        }
        if (imported.Count == 0)
        {
            ProfileErrorText = L.F("Archive.NoPads", Path.GetFileName(path));
            return;
        }
        ProfileErrorText = "";

        // Keys: what is taken stays where it is; an imported pad gives up its key instead.
        var used = new HashSet<HotkeyGesture>(
            (replace ? FunctionKeys.Select(f => HotkeyGesture.Parse(FunctionKeyToken(f.OwnerId))) : AllKeys().Select(k => k.Gesture))
                .Where(g => g is not null).Select(g => g!.Value));
        foreach (var c in imported)
            if (HotkeyGesture.Parse(c.Hotkey) is { } g && !used.Add(g)) c.Hotkey = null;

        var before = _profile.Tiles.ToList();
        SetTiles(replace ? imported : [.. before, .. imported]);
        var added = Tiles.Where(t => imported.Contains(t.Config)).ToList();
        int n = added.Count;
        var backupName = Path.GetFileName(backup);
        PushUndo(() => L.Count(replace ? "Notice.ImportedReplace" : "Notice.ImportedAdd", n, backupName), () => SetTiles(before));
        ProfileResultText = L.Count(replace ? "Notice.ImportedReplace" : "Notice.ImportedAdd", n, backupName);
        ShowAdded(added);
        if (EvenOutOnImport) await EvenOutAsync(added, remember: false);
    }

    /// <summary>The current pads as a ZIP in the backup folder, before an import changes them.</summary>
    private string BackupProfile()
    {
        TrySave();
        var file = Path.Combine(_paths.BackupsDir, $"vor-import-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        ProfileArchive.Export(_profile, _paths, file);
        return file;
    }

    /// <summary>Replaces every pad (import, undo of an import). Files stay where they are.</summary>
    private void SetTiles(List<TileConfig> configs)
    {
        StopAll();
        foreach (var t in Tiles)
        {
            _bank.Remove(t.Config.Id);
            _hotkeys.Remove(t.OwnerId);
            ClearNotice("hotkey-" + t.Config.Id);
        }
        Tiles.Clear();
        _profile.Tiles.Clear();
        _profile.Tiles.AddRange(configs);
        foreach (var c in configs)
        {
            var tile = new TileViewModel(this, c);
            Tiles.Add(tile);
            _ = LoadTileAsync(tile);
            _ = LoadTileImageAsync(tile);
        }
        ApplyAllHotkeys();
        RecolorAutomatic();
        ScheduleSave();
    }

    // ---------------------------------------------------------------- even out volume

    private readonly ConditionalWeakTable<SoundClip, StrongBox<LoudnessInfo>> _loudness = [];

    private LoudnessInfo LoudnessOf(SoundClip clip)
    {
        if (_loudness.TryGetValue(clip, out var box)) return box.Value;
        var info = Loudness.Analyze(clip.Samples, clip.Format.Channels, clip.Format.SampleRate);
        _loudness.AddOrUpdate(clip, new StrongBox<LoudnessInfo>(info));
        return info;
    }

    /// <summary>Right-click → "Even out volume": this pad to the level of the others.</summary>
    public void EvenOut(TileViewModel tile)
    {
        if (tile.Clip is null)
        {
            ShowNotice("play", TF(tile.State == TileState.Error ? "Notice.EvenOutBroken" : "Notice.StillLoading", tile.Name));
            return;
        }
        _ = EvenOutAsync([tile], remember: true);
    }

    /// <summary>Right-click → "Even out all pads".</summary>
    public void EvenOutAll() => _ = EvenOutAsync(Tiles.ToList(), remember: true);

    /// <summary>
    /// Measures the sounds (once per sound) and sets the pad volume of <paramref name="which"/> so they
    /// sound as loud as the middle pad; only the volume field changes, never the file.
    /// </summary>
    private async Task EvenOutAsync(IReadOnlyList<TileViewModel> which, bool remember)
    {
        // Pads still loading are waited for (at most 30 s).
        for (int i = 0; i < 300 && which.Any(t => t.State == TileState.Loading); i++) await Task.Delay(100);
        var clips = Tiles.Where(t => t.Clip is not null).Select(t => (Tile: t, Clip: t.Clip!)).ToList();
        if (clips.Count == 0) return;
        var infos = await Task.Run(() => clips.Select(c => (c.Tile, Info: LoudnessOf(c.Clip))).ToList());
        if (_disposed) return;
        double target = Loudness.Target(infos.Select(i => i.Info));
        var old = new List<(TileViewModel Tile, double Percent)>();
        foreach (var (tile, info) in infos)
        {
            if (!which.Contains(tile) || Loudness.VolumeFor(info, target) is not { } v) continue;
            double percent = Math.Round(v * 100);
            if (Math.Abs(tile.VolumePercent - percent) < 0.5) continue;
            old.Add((tile, tile.VolumePercent));
            tile.VolumePercent = percent;
        }
        if (!remember) return;
        if (old.Count == 0)
        {
            ShowNotice("evenout", T("Notice.EvenOutNothing"));
            HideNoticeAfter("evenout", TimeSpan.FromSeconds(4));
            return;
        }
        Func<string> text = old.Count == 1 && which.Count == 1
            ? () => L.F("Notice.EvenedOutOne", old[0].Tile.Name, L.Percent(old[0].Percent / 100), old[0].Tile.VolumeText)
            : () => L.Count("Notice.EvenedOut", old.Count);
        PushUndo(text, () =>
        {
            foreach (var (tile, percent) in old) tile.VolumePercent = percent;
        });
    }
}
