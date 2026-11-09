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

/// <summary>Profile as a ZIP file (export, import with preview).</summary>
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _importPath = null;
            ImportPreview = null;
            ProfileErrorText = L.F("Archive.CannotOpen", Path.GetFileName(path), ex.Message);
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
        ImportedPads result;
        try
        {
            backup = BackupProfile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProfileErrorText = L.F("Profile.BackupFailed", ex.Message);
            return;
        }
        try
        {
            result = ProfileArchive.Import(path, _paths);
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
        var imported = result.Tiles;
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
        var beforeCategories = _profile.Categories.ToList();
        var categories = replace ? result.Categories : MergeCategories(beforeCategories, result.Categories, imported);
        SetTiles(replace ? imported : [.. before, .. imported], categories);
        var added = Tiles.Where(t => imported.Contains(t.Config)).ToList();
        int n = added.Count;
        var backupName = Path.GetFileName(backup);
        PushUndo(() => L.Count(replace ? "Notice.ImportedReplace" : "Notice.ImportedAdd", n, backupName), () => SetTiles(before, beforeCategories));
        ProfileResultText = L.Count(replace ? "Notice.ImportedReplace" : "Notice.ImportedAdd", n, backupName);
        ShowAdded(added);
    }

    /// <summary>The current pads as a ZIP in the backup folder, before an import changes them.</summary>
    private string BackupProfile()
    {
        TrySave();
        var file = Path.Combine(_paths.BackupsDir, $"vor-import-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        ProfileArchive.Export(_profile, _paths, file);
        return file;
    }

    /// <summary>
    /// Categories after adding imported pads: an imported category with the name of one already here goes into that
    /// one (its pads are pointed to it), the others are added at the end.
    /// </summary>
    private static List<CategoryConfig> MergeCategories(List<CategoryConfig> here, List<CategoryConfig> imported, List<TileConfig> pads)
    {
        var merged = here.ToList();
        foreach (var c in imported)
        {
            var same = here.FirstOrDefault(h => string.Equals(h.Name, c.Name, StringComparison.CurrentCultureIgnoreCase));
            if (same is null)
            {
                merged.Add(c);
                continue;
            }
            foreach (var p in pads.Where(p => p.Category == c.Id)) p.Category = same.Id;
        }
        return merged;
    }

    /// <summary>Replaces every pad and the categories (import, undo of an import). Files stay where they are.</summary>
    private void SetTiles(List<TileConfig> configs, List<CategoryConfig> categories)
    {
        StopAll();
        // Older steps refer to pads that are gone now; only the import itself can be taken back.
        _undo.Clear();
        ClearNotice("undo");
        foreach (var t in Tiles)
        {
            _bank.Remove(t.Config.Id);
            _hotkeys.Remove(t.OwnerId);
            ClearNotice("hotkey-" + t.Config.Id);
        }
        Tiles.Clear();
        _profile.Tiles.Clear();
        _profile.Tiles.AddRange(configs);
        _profile.Categories = categories.ToList();
        _profile.Migrate(); // pads pointing to a category that is not there are in none
        foreach (var c in configs)
        {
            var tile = new TileViewModel(this, c);
            Tiles.Add(tile);
            _ = LoadTileAsync(tile);
            _ = LoadTileImageAsync(tile);
        }
        KeepGroupsTogether();
        RefreshGroups();
        RefreshMoveMenus();
        ApplyAllHotkeys();
        ScheduleSave();
    }
}
