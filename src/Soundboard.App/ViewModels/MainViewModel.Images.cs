using System.IO;
using Microsoft.Win32;
using Soundboard.App.Images;
using Soundboard.Core.Images;

namespace Soundboard.App.ViewModels;

/// <summary>Pictures as pad backgrounds: choose, drop, load, remove, tidy up.</summary>
public sealed partial class MainViewModel
{
    private const string WebpWayOut = "Speichere das Bild als PNG oder JPG, oder installiere die kostenlosen „WebP-Bilderweiterungen“ aus dem Microsoft Store.";

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
    /// "Bilder-Ordner öffnen" in the pad menu: the picture folder in Explorer (created if missing),
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
            ShowNotice("image", $"Der Bilder-Ordner ließ sich nicht öffnen ({ex.Message}).", "Erneut versuchen", () => ShowTileImageInFolder(tile));
        }
    }

    /// <summary>"Bild wählen …" in the pad menu.</summary>
    public void ChooseImageDialog(TileViewModel tile)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Bild für „{tile.Name}“ wählen",
            Multiselect = false,
            Filter = ImageFiles.DialogFilter,
        };
        if (dlg.ShowDialog() == true) _ = AssignImageAsync(tile, dlg.FileName);
    }

    /// <summary>
    /// Gives a pad a picture: checks and decodes it first (nothing is copied if it cannot be shown),
    /// then copies it into the picture folder. The original stays where it is. Never throws:
    /// every problem ends in a yellow notice with a way out.
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
                ShowNotice("image", result.Problem switch
                {
                    ImageProblem.NoWebpCodec => $"„{fileName}“ ist ein WebP-Bild, und Windows kann WebP auf diesem Rechner nicht lesen. {WebpWayOut}",
                    ImageProblem.Missing => $"Die Bilddatei „{fileName}“ gibt es nicht (mehr). Wurde sie verschoben oder gelöscht?",
                    _ => $"„{fileName}“ lässt sich nicht als Bild lesen – die Datei ist beschädigt oder hat eine falsche Endung. Möglich sind {ImageFiles.FormatsText}.",
                }, "Anderes Bild wählen", retry);
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
                ShowNotice("image", $"Bild von „{tile.Name}“ ersetzt.", "Rückgängig", () => RestoreImage(config, old));
            }
            else
            {
                ClearNotice("image");
            }
        }
        catch (ImageImportException ex)
        {
            ShowNotice("image", ex.Message, "Anderes Bild wählen", retry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("image", $"„{fileName}“ konnte nicht in den Bilder-Ordner kopiert werden ({ex.Message}).", "Erneut versuchen", () => _ = AssignImageAsync(tile, source));
        }
        catch (Exception ex)
        {
            ShowNotice("image", $"„{fileName}“ ließ sich nicht als Bild übernehmen ({ex.Message}).", "Anderes Bild wählen", retry);
        }
    }

    /// <summary>"Bild entfernen": the pad shows its colour again; "Rückgängig" brings the picture back.</summary>
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
        ShowNotice("image", $"Bild von „{tile.Name}“ entfernt.", "Rückgängig", () => RestoreImage(config, old));
    }

    /// <summary>"Rückgängig" for a removed or replaced picture. Finds the pad by its settings, so it also works after the pad itself was removed and brought back.</summary>
    private void RestoreImage(Soundboard.Core.Config.TileConfig config, string old)
    {
        var tile = Tiles.FirstOrDefault(t => ReferenceEquals(t.Config, config));
        if (tile is null)
        {
            ShowNotice("image", "Das Pad gibt es nicht mehr, deshalb lässt sich sein Bild nicht zurückholen.");
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
                    ShowNotice("image-many", $"Ein Pad zeigt ein Bild – genommen wurde „{Path.GetFileName(pictures[0])}“. Die anderen Bilder zieh einzeln auf andere Pads.");
            }
            else
            {
                ShowNotice("image-many", Tiles.Count == 0
                    ? "Bilder kommen auf Pads. Zieh zuerst Sounds hierher; danach ziehst du ein Bild direkt auf ein Pad."
                    : "Bilder auf ein Pad ziehen, Sounds auf die freie Fläche. Das Bild wird dann der Hintergrund dieses Pads.");
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
