using System.Text.RegularExpressions;

namespace Soundboard.Core.Web;

/// <summary>
/// Recognises a YouTube video link in pasted or dropped text (watch, youtu.be, shorts, music,
/// embed, live) and turns it into the plain watch link. Playlists are not followed: only the video.
/// </summary>
public static partial class YouTubeLink
{
    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoId();

    /// <summary>The 11-character video id, or null if the text is not one YouTube video link.</summary>
    public static string? VideoIdOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Browsers put the link alone on the first line; some add the page title on the next.
        var first = text.Trim().Split('\n', 2)[0].Trim();
        if (!first.Contains("://", StringComparison.Ordinal)) first = "https://" + first;
        if (!Uri.TryCreate(first, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        else if (host.StartsWith("m.", StringComparison.Ordinal)) host = host[2..];

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = null;
        if (host == "youtu.be")
        {
            if (segments.Length >= 1) id = segments[0];
        }
        else if (host is "youtube.com" or "music.youtube.com" or "youtube-nocookie.com")
        {
            if (segments.Length == 1 && segments[0] == "watch") id = QueryValue(uri.Query, "v");
            else if (segments.Length >= 2 && segments[0] is "shorts" or "embed" or "live" or "v") id = segments[1];
        }
        return id is not null && VideoId().IsMatch(id) ? id : null;
    }

    /// <summary>The plain watch link for a video id.</summary>
    public static string WatchUrl(string videoId) => "https://www.youtube.com/watch?v=" + videoId;

    private static string? QueryValue(string query, string name)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0 && part[..eq] == name) return Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return null;
    }
}
