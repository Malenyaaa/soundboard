using System.Security.Cryptography;
using System.Text.Json;

namespace Soundboard.Core.Web;

/// <summary>A newer version on GitHub: its number and the setup file to fetch.</summary>
public sealed record UpdateRelease(Version Version, string Tag, string SetupUrl, string SetupName, long Size, string Sha256);

/// <summary>
/// Automatic updates from the GitHub releases of the project (latest stable release only, no
/// pre-releases). The setup file is fetched into the update folder and checked against the
/// SHA-256 that GitHub lists for the file; the app runs it silently when it ends.
/// Nothing here changes the installation; failures (offline, rate limit) are quiet: the next
/// check simply tries again.
/// </summary>
public sealed class AppUpdater(string updatesDir, Version current, HttpClient? http = null)
{
    public const string Repository = "Malenyaaa/soundboard";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";

    private readonly HttpClient _http = http ?? CreateHttp();

    public Version Current { get; } = Normalize(current);

    /// <summary>The newest stable release if it is newer than this copy; otherwise null. Throws on network errors.</summary>
    public async Task<UpdateRelease?> CheckAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null; // 404 = no release yet, 403 = rate limit
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var release = ParseRelease(json);
        return release is not null && release.Version > Current ? release : null;
    }

    /// <summary>
    /// Fetches the setup of <paramref name="release"/> (or takes the one fetched before) and
    /// returns its path once the SHA-256 matches. Older setups in the folder are removed.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateRelease release, CancellationToken ct)
    {
        Directory.CreateDirectory(updatesDir);
        var target = Path.Combine(updatesDir, release.SetupName);
        foreach (var old in Directory.EnumerateFiles(updatesDir))
            if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase)) TryDelete(old);

        if (File.Exists(target) && await HashMatchesAsync(target, release.Sha256, ct).ConfigureAwait(false))
            return target;

        var part = target + ".part";
        try
        {
            using (var response = await _http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(part);
                await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
            }
            if (!await HashMatchesAsync(part, release.Sha256, ct).ConfigureAwait(false))
                throw new InvalidDataException("SHA-256 of the downloaded setup does not match.");
            File.Move(part, target, overwrite: true);
            return target;
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    /// <summary>
    /// Reads the answer of the "latest release" API. Null when it is a draft or pre-release, the
    /// tag is no version ("v1.2.3"), there is no setup file ("Soundboard-Setup-*.exe") or GitHub
    /// lists no SHA-256 for it (then the file cannot be checked and is not used).
    /// </summary>
    public static UpdateRelease? ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
            if (!root.TryGetProperty("tag_name", out var tagEl) || tagEl.GetString() is not { } tag) return null;
            if (ParseVersion(tag) is not { } version) return null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Str(asset, "name");
                if (name is null || !IsSetupName(name)) continue;
                var url = Str(asset, "browser_download_url");
                var digest = Str(asset, "digest");
                if (url is null || !url.StartsWith("https://github.com/" + Repository + "/releases/download/", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
                var sha = digest[7..].ToLowerInvariant();
                if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) return null;
                long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0;
                return new UpdateRelease(version, tag, url, name, size, sha);
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"v1.2.3" or "1.2.3" → 1.2.3.0; null for anything else (also "1.2.3-beta").</summary>
    public static Version? ParseVersion(string tag)
    {
        var t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        if (t.Count(c => c == '.') is < 1 or > 3 || !t.All(c => char.IsAsciiDigit(c) || c == '.')) return null;
        return Version.TryParse(t, out var v) ? Normalize(v) : null;
    }

    internal static bool IsSetupName(string name) =>
        name.StartsWith("Soundboard-Setup-", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.Contains("..", StringComparison.Ordinal);

    /// <summary>Missing parts count as 0, so 1.0 and 1.0.0.0 are equal.</summary>
    private static Version Normalize(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static async Task<bool> HashMatchesAsync(string path, string sha256, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash) == sha256;
    }

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Soundboard-Updater");
        return http;
    }
}
