using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Text;
using Soundboard.Core.Web;

namespace Soundboard.App.ViewModels;

/// <summary>
/// A YouTube link becomes a pad: Ctrl+V in the window, the link dragged in from the browser, or
/// "Paste YouTube link" in the context menu. The sound is fetched with yt-dlp (see
/// <see cref="YouTubeDownloader"/>) and then added like a dropped file, named after the video.
/// </summary>
public sealed partial class MainViewModel
{
    private YouTubeDownloader? _youTube;
    private readonly CancellationTokenSource _youTubeStop = new();
    private readonly Dictionary<string, double> _youTubeRunning = [];

    /// <summary>Small download display in the status line, e.g. "↓ 42 %"; empty while nothing downloads.</summary>
    [ObservableProperty]
    private string _youTubeProgressText = "";

    private void UpdateYouTubeProgress()
    {
        if (_youTubeRunning.Count == 0)
        {
            YouTubeProgressText = "";
            return;
        }
        string percent = L.Percent(_youTubeRunning.Values.Average());
        YouTubeProgressText = _youTubeRunning.Count == 1 ? "↓ " + percent : $"↓ {percent} ({_youTubeRunning.Count})";
    }

    /// <summary>Starts the download if <paramref name="text"/> is a YouTube video link. False: no link.</summary>
    public bool TryAddYouTube(string? text)
    {
        if (YouTubeLink.VideoIdOf(text) is not { } id) return false;
        _ = AddYouTubeAsync(id);
        return true;
    }

    /// <summary>Ctrl+V outside a text field: files from the clipboard become pads, a YouTube link too.</summary>
    [RelayCommand]
    private void PasteSounds()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                if (files.Count > 0)
                {
                    AddFiles(files);
                    return;
                }
            }
            if (Clipboard.ContainsText() && TryAddYouTube(Clipboard.GetText())) return;
        }
        catch (COMException)
        {
            // Another program holds the clipboard right now: same hint as an empty clipboard.
        }
        ShowNotice("youtube", T("YouTube.NoLink"));
    }

    private async Task AddYouTubeAsync(string id)
    {
        if (_disposed || !_youTubeRunning.TryAdd(id, 0)) return;
        string key = "youtube-" + id;
        ClearNotice("youtube");
        ClearNotice(key);
        UpdateYouTubeProgress();
        _youTube ??= new YouTubeDownloader(_paths.ToolsDir);
        string work = Path.Combine(Path.GetTempPath(), "Soundboard-YouTube-" + Guid.NewGuid().ToString("N")[..8]);
        // Progress posts to this (UI) thread; a late report after the end is dropped.
        var progress = new Progress<double>(share =>
        {
            if (!_youTubeRunning.ContainsKey(id)) return;
            _youTubeRunning[id] = share;
            UpdateYouTubeProgress();
        });
        try
        {
            string file = await _youTube.DownloadAudioAsync(id, work, progress, _youTubeStop.Token);
            if (_disposed) return;
            AddFiles([file]);
        }
        catch (YouTubeImportException ex)
        {
            if (_disposed) return;
            string message = ex.Message;
            ShowNotice(key, () => message, T("Action.Retry"), () => _ = AddYouTubeAsync(id));
        }
        catch (OperationCanceledException)
        {
            // the app closes
        }
        finally
        {
            _youTubeRunning.Remove(id);
            if (!_disposed) UpdateYouTubeProgress();
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>At exit: a running yt-dlp is ended.</summary>
    private void StopYouTube()
    {
        _youTubeStop.Cancel();
    }
}
