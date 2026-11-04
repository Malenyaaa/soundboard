using System.IO;
using Microsoft.Win32;
using Soundboard.App.Images;
using Soundboard.Core.Images;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>Pictures as pad backgrounds: choose, drop, load, remove, tidy up.</summary>
public sealed partial class MainViewModel
{

    public string ImagesDir => _paths.ImagesDir;

    /// <summary>
    /// Loads the picture of a pad in the background. A missing or unreadable file is no warning:
    /// the pad quietly shows its colour and plays as usual. The entry in the profile stays, so the
    /// picture is back as soon as the file is.
    /// </summary>
    private async Task LoadTileImageAsync(TileViewModel tile)
    {
        int version = ++tile.ImageLoadVersion;
        var name = tile.Config.Image;
        tile.RefreshImageConfigured();
        if (name is null)
        {
            tile.Image = null;
            return;
        }

        System.Windows.Media.Imaging.BitmapSource? image;
        try
        {
            var path = _paths.ImagePath(name);
            image = path is null ? null : (await Task.Run(() => ImageLoader.Load(path))).Image;
        }
        catch (Exception)
        {
            image = null;
        }
        if (version != tile.ImageLoadVersion || _disposed) return; // superseded
        tile.Image = image;
    }

    /// <summary>
    /// "Open picture folder" in the pad menu: the picture folder in Explorer (created if missing),
    /// with this pad's picture selected when it has one that is there.
    /// </summary>
    public void ShowTileImageInFolder(TileViewModel tile)
    {
        try
        {
            var path = _paths.ImagePath(tile.Config.Image);
            if (path is not null && File.Exists(path))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                return;
            }
            Directory.CreateDirectory(_paths.ImagesDir);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{_paths.ImagesDir}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowNotice("image", TF("Notice.ImageFolderFailed", ex.Message), T("Action.Retry"), () => ShowTileImageInFolder(tile));
        }
    }

    /// <summary>"Choose picture…" in the pad menu.</summary>
    public void ChooseImageDialog(TileViewModel tile)
    {
        var dlg = new OpenFileDialog
        {
            Title = L.F("Dialog.ChoosePicture", tile.Name),
            Multiselect = false,
            Filter = ImageFiles.DialogFilter,
        };
        if (dlg.ShowDialog() == true) _ = AssignImageAsync(tile, dlg.FileName);
    }

    /// <summary>
    /// Gives a pad a picture: checks and decodes it first (nothing is copied if it cannot be shown),
    /// then copies it into the picture folder. The original stays where it is. Never throws:
    /// every problem ends in a notice with a way out.
    /// </summary>
    public async Task AssignImageAsync(TileViewModel tile, string source)
    {
        string fileName = Path.GetFileName(source);
        Action retry = () => ChooseImageDialog(tile);
        try
        {
            ImageFiles.Check(source);

            int version = ++tile.ImageLoadVersion;
            var result = await Task.Run(() => ImageLoader.Load(source));
            if (_disposed || version != tile.ImageLoadVersion || !Tiles.Contains(tile)) return;
            if (result.Problem != ImageProblem.None)
            {
                var problem = result.Problem;
                ShowNotice("image", () => problem switch
                {
                    ImageProblem.NoWebpCodec => L.F("Notice.ImageNoWebp", fileName),
                    ImageProblem.Missing => L.F("Image.Missing", fileName),
                    _ => L.F("Image.Unreadable", fileName, ImageFiles.FormatsText),
                }, T("Action.OtherPicture"), retry);
                return;
            }

            string stored = _paths.ImportImage(source);
            var old = tile.Config.Image;
            var config = tile.Config;
            config.Image = stored;
            tile.Image = result.Image;
            tile.RefreshImageConfigured();
            ScheduleSave();
            if (old is not null && !string.Equals(old, stored, StringComparison.OrdinalIgnoreCase))
            {
                _paths.ReleaseImage(old);
                ShowNotice("image", TF("Notice.ImageReplaced", tile.Name), T("Action.Undo"), () => RestoreImage(config, old));
            }
            else
            {
                ClearNotice("image");
            }
        }
        catch (ImageImportException ex)
        {
            var message = ex.Message;
            ShowNotice("image", () => message, T("Action.OtherPicture"), retry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("image", TF("Notice.ImageCopyFailed", fileName, ex.Message), T("Action.Retry"), () => _ = AssignImageAsync(tile, source));
        }
        catch (Exception ex)
        {
            ShowNotice("image", TF("Notice.ImageTakeFailed", fileName, ex.Message), T("Action.OtherPicture"), retry);
        }
    }

    /// <summary>"Remove picture": the pad shows its colour again; "Undo" brings the picture back.</summary>
    public void RemoveImage(TileViewModel tile)
    {
        var old = tile.Config.Image;
        if (old is null) return;
        var config = tile.Config;
        tile.ImageLoadVersion++;
        config.Image = null;
        tile.Image = null;
        tile.RefreshImageConfigured();
        _paths.ReleaseImage(old);
        ScheduleSave();
        ShowNotice("image", TF("Notice.ImageRemoved", tile.Name), T("Action.Undo"), () => RestoreImage(config, old));
    }

    /// <summary>"Undo" for a removed or replaced picture. Finds the pad by its settings, so it also works after the pad itself was removed and brought back.</summary>
    private void RestoreImage(Soundboard.Core.Config.TileConfig config, string old)
    {
        var tile = Tiles.FirstOrDefault(t => ReferenceEquals(t.Config, config));
        if (tile is null)
        {
            ShowNotice("image", T("Notice.ImagePadGone"));
            return;
        }
        config.Image = old;
        _ = LoadTileImageAsync(tile);
        ScheduleSave();
    }

    /// <summary>
    /// Files dropped on the window. A picture dropped on a pad becomes its background; sounds
    /// become new pads wherever they land. A picture on the free area gets a hint where it belongs.
    /// </summary>
    public void DropFiles(IReadOnlyList<string> paths, TileViewModel? onTile)
    {
        var pictures = paths.Where(p => File.Exists(p) && ImageFiles.HasImageExtension(p)).ToList();
        var rest = paths.Except(pictures).ToList();

        if (pictures.Count > 0)
        {
            if (onTile is not null)
            {
                _ = AssignImageAsync(onTile, pictures[0]);
                if (pictures.Count > 1)
                    ShowNotice("image-many", TF("Notice.ImageOnlyOne", Path.GetFileName(pictures[0])));
            }
            else
            {
                ShowNotice("image-many", T(Tiles.Count == 0 ? "Notice.ImageNoPads" : "Notice.ImageOnPad"));
            }
        }
        if (rest.Count > 0) AddFiles(rest);
    }

    /// <summary>
    /// Deletes the pictures the app let go of (removed, replaced, pad removed) once no pad uses
    /// them. Only after a clean load, and at exit only after a successful save.
    /// </summary>
    private void RemoveReleasedImages()
    {
        if (!_imagesTidyAllowed) return;
        _paths.RemoveReleasedImages(_profile.Tiles.Select(t => t.Image));
    }
}
