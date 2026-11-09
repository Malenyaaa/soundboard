using System.Diagnostics;
using System.Text;
using Soundboard.Core.Audio;
using Soundboard.Core.Text;

namespace Soundboard.Core.Web;

/// <summary>A YouTube sound could not be fetched. Message is a user-facing hint in the language of the interface.</summary>
public sealed class YouTubeImportException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Fetches the audio track of a YouTube video with yt-dlp (https://github.com/yt-dlp/yt-dlp, Unlicense).
/// yt-dlp is not shipped with the app: on first use it is downloaded from its GitHub releases into the
/// tool folder of the app, and when a download fails it updates itself once (YouTube changes often).
/// Only the M4A (AAC) track is taken, which Media Foundation decodes; so no ffmpeg is needed.
/// </summary>
public sealed class YouTubeDownloader(string toolsDir)
{
    public const string ToolUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    private static readonly HttpClient Http = CreateHttp();
    private static readonly SemaphoreSlim ToolLock = new(1, 1);
    private bool _updatedThisRun;

    public string ExePath { get; } = Path.Combine(toolsDir, "yt-dlp.exe");

    /// <summary>
    /// Downloads the sound of <paramref name="videoId"/> into <paramref name="workDir"/> (created, should be empty)
    /// and returns the path of the file; its name (without ending) is the video title.
    /// <paramref name="progress"/> gets the share of the sound downloaded so far (0..1).
    /// Throws <see cref="YouTubeImportException"/> with a hint for the user.
    /// </summary>
    public async Task<string> DownloadAudioAsync(string videoId, string workDir, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(workDir);
        await EnsureToolAsync(ct).ConfigureAwait(false);

        void OnLine(string line)
        {
            if (ParseProgress(line) is { } share) progress?.Report(share);
        }
        var result = await RunAsync(DownloadArgs(videoId, workDir), ct, OnLine).ConfigureAwait(false);
        if (result.ExitCode != 0 && !_updatedThisRun)
        {
            // Old yt-dlp versions stop working when YouTube changes something: update and try once more.
            _updatedThisRun = true;
            progress?.Report(0);
            await UpdateToolAsync(ct).ConfigureAwait(false);
            ClearDirectory(workDir);
            result = await RunAsync(DownloadArgs(videoId, workDir), ct, OnLine).ConfigureAwait(false);
        }
        if (result.ExitCode != 0)
            throw new YouTubeImportException(L.F("YouTube.Failed", LastError(result.Error)));

        var file = Directory.EnumerateFiles(workDir).FirstOrDefault(SoundDecoder.IsSupported);
        if (file is null)
        {
            // yt-dlp skips a video quietly when the filter does not match (too long, live).
            if (result.Output.Contains("does not pass filter", StringComparison.OrdinalIgnoreCase))
                throw new YouTubeImportException(L.F("YouTube.TooLong", (int)SoundDecoder.MaxDuration.TotalMinutes));
            throw new YouTubeImportException(L.Get("YouTube.NoAudio"));
        }
        return file;
    }

    internal static List<string> DownloadArgs(string videoId, string workDir) =>
    [
        "--ignore-config", "--no-playlist", "--no-mtime", "--no-part",
        "--encoding", "utf-8",
        // One progress line each, easy to read: marker, bytes so far, total, estimated total.
        "--newline", "--progress-template", "download:" + ProgressMarker
            + " %(progress.downloaded_bytes)s %(progress.total_bytes)s %(progress.total_bytes_estimate)s",
        // Only the AAC track: Media Foundation decodes it, nothing to convert.
        "-f", "bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]",
        "--match-filter", $"duration <= {(int)SoundDecoder.MaxDuration.TotalSeconds} & !is_live",
        "--windows-filenames", "--trim-filenames", "100",
        "-P", workDir,
        "-o", "%(title)s.%(ext)s",
        YouTubeLink.WatchUrl(videoId),
    ];

    private const string ProgressMarker = "SBPROGRESS";

    /// <summary>Share downloaded (0..1) from one progress line of yt-dlp; null for other lines or no size known.</summary>
    internal static double? ParseProgress(string line)
    {
        int at = line.IndexOf(ProgressMarker, StringComparison.Ordinal);
        if (at < 0) return null;
        var parts = line[(at + ProgressMarker.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        static double? Num(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) && v >= 0 ? v : null;
        if (Num(parts[0]) is not { } done) return null;
        if ((Num(parts[1]) ?? Num(parts[2])) is not { } total || total <= 0) return null;
        return Math.Clamp(done / total, 0, 1);
    }

    /// <summary>Downloads yt-dlp if it is not in the tool folder yet.</summary>
    private async Task EnsureToolAsync(CancellationToken ct)
    {
        if (File.Exists(ExePath)) return;
        await ToolLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(ExePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ExePath)!);
            var part = ExePath + ".part";
            try
            {
                using (var response = await Http.GetAsync(ToolUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using var target = File.Create(part);
                    await response.Content.CopyToAsync(target, ct).ConfigureAwait(false);
                }
                File.Move(part, ExePath, overwrite: true);
                _updatedThisRun = true; // just fetched: already the newest version
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                           || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                TryDelete(part);
                throw new YouTubeImportException(L.F("YouTube.ToolFailed", ex.Message), ex);
            }
        }
        finally
        {
            ToolLock.Release();
        }
    }

    private async Task UpdateToolAsync(CancellationToken ct)
    {
        await ToolLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // A failed update is no error of its own: the second try then shows the real reason.
            await RunAsync(["--ignore-config", "-U"], ct).ConfigureAwait(false);
        }
        finally
        {
            ToolLock.Release();
        }
    }

    private sealed record RunResult(int ExitCode, string Output, string Error);

    private async Task<RunResult> RunAsync(IEnumerable<string> args, CancellationToken ct, Action<string>? onLine = null)
    {
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(ExePath)!,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new YouTubeImportException(L.F("YouTube.ToolFailed", ex.Message), ex);
        }
        var output = ReadLinesAsync(process.StandardOutput, onLine);
        var error = ReadLinesAsync(process.StandardError, onLine);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return new RunResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    /// <summary>Reads a stream line by line (progress arrives while yt-dlp runs) and returns all of it.</summary>
    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<string>? onLine)
    {
        var all = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            all.AppendLine(line);
            onLine?.Invoke(line);
        }
        return all.ToString();
    }

    /// <summary>The last "ERROR:" line of yt-dlp without the prefix; otherwise the last line.</summary>
    internal static string LastError(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.LastOrDefault(l => l.StartsWith("ERROR:", StringComparison.Ordinal)) ?? lines.LastOrDefault() ?? "";
        if (error.StartsWith("ERROR:", StringComparison.Ordinal)) error = error[6..].Trim();
        // "[youtube] abc: Video unavailable" → "Video unavailable"
        if (error.StartsWith('[') && error.IndexOf(": ", StringComparison.Ordinal) is var i and > 0) error = error[(i + 2)..];
        return error.Length > 200 ? error[..200] + " …" : error;
    }

    private static void ClearDirectory(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir)) TryDelete(f);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Soundboard");
        return http;
    }
}
