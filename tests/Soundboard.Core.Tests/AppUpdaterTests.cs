using System.Net;
using System.Security.Cryptography;
using System.Text;
using Soundboard.Core.Web;

namespace Soundboard.Core.Tests;

[TestClass]
public sealed class AppUpdaterTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Json(string tag, bool pre = false, string? digest = "sha256:" + Sha,
        string name = "Soundboard-Setup-1.2.0.exe", string? url = null)
    {
        url ??= $"https://github.com/{AppUpdater.Repository}/releases/download/{tag}/{name}";
        string d = digest is null ? "null" : $"\"{digest}\"";
        return $$"""
            {"tag_name":"{{tag}}","draft":false,"prerelease":{{(pre ? "true" : "false")}},
             "assets":[{"name":"notes.txt","browser_download_url":"https://example.com/x","digest":null},
                       {"name":"{{name}}","browser_download_url":"{{url}}","size":123,"digest":{{d}}}]}
            """;
    }

    [TestMethod]
    [DataRow("v1.2.3", "1.2.3.0")]
    [DataRow("1.0", "1.0.0.0")]
    [DataRow("V2.0.1", "2.0.1.0")]
    public void ParsesVersionTags(string tag, string expected) =>
        Assert.AreEqual(Version.Parse(expected), AppUpdater.ParseVersion(tag));

    [TestMethod]
    [DataRow("v1.2.3-beta")]
    [DataRow("latest")]
    [DataRow("v1")]
    [DataRow("")]
    public void RejectsOtherTags(string tag) => Assert.IsNull(AppUpdater.ParseVersion(tag));

    [TestMethod]
    public void ReadsSetupAndDigest()
    {
        var r = AppUpdater.ParseRelease(Json("v1.2.0"));
        Assert.IsNotNull(r);
        if (r is null) return;
        Assert.AreEqual(new Version(1, 2, 0, 0), r.Version);
        Assert.AreEqual("Soundboard-Setup-1.2.0.exe", r.SetupName);
        Assert.AreEqual(Sha, r.Sha256);
        Assert.AreEqual(123L, r.Size);
    }

    [TestMethod]
    public void IgnoresPreReleases() => Assert.IsNull(AppUpdater.ParseRelease(Json("v1.2.0", pre: true)));

    [TestMethod]
    public void NeedsDigest() => Assert.IsNull(AppUpdater.ParseRelease(Json("v1.2.0", digest: null)));

    [TestMethod]
    public void NeedsDownloadFromOwnRepository() =>
        Assert.IsNull(AppUpdater.ParseRelease(Json("v1.2.0", url: "https://evil.example/Soundboard-Setup-1.2.0.exe")));

    [TestMethod]
    [DataRow("..\\Soundboard-Setup-1.exe")]
    [DataRow("Soundboard-Setup-1.msi")]
    [DataRow("Other-Setup-1.exe")]
    public void SetupNameMustFit(string name) => Assert.IsNull(AppUpdater.ParseRelease(Json("v1.2.0", name: name)));

    [TestMethod]
    public void BrokenJsonIsNoRelease() => Assert.IsNull(AppUpdater.ParseRelease("{not json"));

    [TestMethod]
    public async Task OffersOnlyNewerVersions()
    {
        using var dir = new TempDir();
        var http = new HttpClient(new FakeHandler(_ => Json("v1.2.0")));
        Assert.IsNotNull(await new AppUpdater(dir.Path, new Version(1, 1, 9), http).CheckAsync(default));
        Assert.IsNull(await new AppUpdater(dir.Path, new Version(1, 2, 0), http).CheckAsync(default));
        Assert.IsNull(await new AppUpdater(dir.Path, new Version(1, 3, 0, 0), http).CheckAsync(default));
    }

    [TestMethod]
    public async Task NoReleaseYetIsNoUpdate()
    {
        using var dir = new TempDir();
        var http = new HttpClient(new FakeHandler(_ => null));
        Assert.IsNull(await new AppUpdater(dir.Path, new Version(1, 0, 0), http).CheckAsync(default));
    }

    [TestMethod]
    public async Task DownloadChecksHash()
    {
        using var dir = new TempDir();
        var body = "setup bytes";
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var http = new HttpClient(new FakeHandler(_ => body));
        var updater = new AppUpdater(dir.Path, new Version(1, 0, 0), http);
        File.WriteAllText(Path.Combine(dir.Path, "Soundboard-Setup-0.9.0.exe"), "old");

        var good = new UpdateRelease(new Version(1, 1, 0, 0), "v1.1.0", "https://x/s.exe", "Soundboard-Setup-1.1.0.exe", 11, sha);
        var path = await updater.DownloadAsync(good, default);
        Assert.AreEqual(body, File.ReadAllText(path));
        Assert.IsFalse(File.Exists(Path.Combine(dir.Path, "Soundboard-Setup-0.9.0.exe")));

        var bad = good with { SetupName = "Soundboard-Setup-1.1.1.exe", Sha256 = Sha };
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => updater.DownloadAsync(bad, default));
        Assert.IsFalse(File.Exists(Path.Combine(dir.Path, "Soundboard-Setup-1.1.1.exe")));
        Assert.IsFalse(File.Exists(Path.Combine(dir.Path, "Soundboard-Setup-1.1.1.exe.part")));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, string?> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = answer(request);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sb-upd-" + Guid.NewGuid().ToString("N")[..8]);
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }
}
