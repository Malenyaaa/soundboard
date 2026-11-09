using Soundboard.Core.Web;

namespace Soundboard.Core.Tests;

/// <summary>Which pasted or dropped texts count as one YouTube video.</summary>
[TestClass]
public sealed class YouTubeLinkTests
{
    [TestMethod]
    [DataRow("https://www.youtube.com/watch?v=jNQXAC9IVRw")]
    [DataRow("https://youtube.com/watch?v=jNQXAC9IVRw&t=12s")]
    [DataRow("https://www.youtube.com/watch?list=PL123&v=jNQXAC9IVRw&index=3")]
    [DataRow("https://m.youtube.com/watch?v=jNQXAC9IVRw")]
    [DataRow("https://music.youtube.com/watch?v=jNQXAC9IVRw&si=abc")]
    [DataRow("https://youtu.be/jNQXAC9IVRw?si=xyz")]
    [DataRow("https://www.youtube.com/shorts/jNQXAC9IVRw")]
    [DataRow("https://www.youtube.com/embed/jNQXAC9IVRw")]
    [DataRow("www.youtube.com/watch?v=jNQXAC9IVRw")]
    [DataRow("  https://youtu.be/jNQXAC9IVRw  \r\n")]
    [DataRow("https://youtu.be/jNQXAC9IVRw\nMe at the zoo - YouTube")]
    public void VideoLinks(string text) =>
        Assert.AreEqual("jNQXAC9IVRw", YouTubeLink.VideoIdOf(text));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("hallo")]
    [DataRow("https://www.youtube.com/")]
    [DataRow("https://www.youtube.com/playlist?list=PL123")]
    [DataRow("https://www.youtube.com/@kanal")]
    [DataRow("https://www.youtube.com/watch?v=zukurz")]
    [DataRow("https://notyoutube.com/watch?v=jNQXAC9IVRw")]
    [DataRow("ftp://youtube.com/watch?v=jNQXAC9IVRw")]
    [DataRow("Sounds/a.mp3")]
    public void NotAVideo(string? text) => Assert.IsNull(YouTubeLink.VideoIdOf(text));

    [TestMethod]
    public void WatchUrl() =>
        Assert.AreEqual("https://www.youtube.com/watch?v=jNQXAC9IVRw", YouTubeLink.WatchUrl("jNQXAC9IVRw"));

    [TestMethod]
    public void LastErrorLine()
    {
        const string stderr = "WARNING: something\nERROR: [youtube] jNQXAC9IVRw: Video unavailable\n";
        Assert.AreEqual("Video unavailable", YouTubeDownloader.LastError(stderr));
        Assert.AreEqual("plain", YouTubeDownloader.LastError("plain\n"));
        Assert.AreEqual("", YouTubeDownloader.LastError(""));
    }

    [TestMethod]
    public void DownloadArgsTakeOnlyTheVideoAsM4a()
    {
        var args = YouTubeDownloader.DownloadArgs("jNQXAC9IVRw", "work");
        CollectionAssert.Contains(args, "--no-playlist");
        Assert.AreEqual("bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]", args[args.IndexOf("-f") + 1]);
        Assert.AreEqual("work", args[args.IndexOf("-P") + 1]);
        Assert.AreEqual("https://www.youtube.com/watch?v=jNQXAC9IVRw", args[^1]);
    }

    [TestMethod]
    public void ProgressLines()
    {
        Assert.AreEqual(0.5, YouTubeDownloader.ParseProgress("SBPROGRESS 500 1000 NA"));
        Assert.AreEqual(0.25, YouTubeDownloader.ParseProgress("SBPROGRESS 250 NA 1000.5")!.Value, 0.001);
        Assert.AreEqual(0.0, YouTubeDownloader.ParseProgress("SBPROGRESS 0 1000 NA"));
        Assert.AreEqual(1.0, YouTubeDownloader.ParseProgress("SBPROGRESS 1200 1000 NA"));
        Assert.IsNull(YouTubeDownloader.ParseProgress("SBPROGRESS 500 NA NA"));
        Assert.IsNull(YouTubeDownloader.ParseProgress("[youtube] jNQXAC9IVRw: Downloading webpage"));
        CollectionAssert.DoesNotContain(YouTubeDownloader.DownloadArgs("jNQXAC9IVRw", "work"), "--no-progress");
    }
}
