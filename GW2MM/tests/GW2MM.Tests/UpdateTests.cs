using System.Net;
using System.Security.Cryptography;
using System.Text;
using GW2MM.Core.Config;
using GW2MM.Core.Updates;

namespace GW2MM.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gw2mm-upd-" + Guid.NewGuid().ToString("N"));
    public UpdateTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string ReleaseJson(string tag, byte[] hostExe, bool prerelease = false, string? digest = null) => $$"""
        {
          "tag_name": "{{tag}}", "draft": false, "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "https://github.com/Beco987/Guild-Wars-2-Multiplayer-Music-Player/releases/tag/{{tag}}",
          "assets": [
            { "name": "GW2MM-Host.exe", "size": {{hostExe.Length}}, "browser_download_url": "https://example.test/host.exe",
              "digest": "{{digest ?? "sha256:" + Convert.ToHexString(SHA256.HashData(hostExe)).ToLowerInvariant()}}" },
            { "name": "GW2MM-Client.exe", "size": 3, "browser_download_url": "https://example.test/client.exe" }
          ]
        }
        """;

    private sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public void ParsesReleasesAndComparesVersions()
    {
        var r = UpdateChecker.Parse(ReleaseJson("v2.1.0", new byte[] { 1, 2 }))!;
        Assert.Equal(new Version(2, 1, 0), r.Version);
        Assert.Equal(2, r.Asset("gw2mm-host.exe")!.Size);
        Assert.NotNull(r.Asset("GW2MM-Host.exe")!.Sha256);
        Assert.Null(r.Asset("GW2MM-Client.exe")!.Sha256);
        Assert.Null(UpdateChecker.Parse(ReleaseJson("v9.0.0", new byte[1], prerelease: true)));
        Assert.Null(UpdateChecker.Parse(ReleaseJson("nightly", new byte[1])));

        Assert.Equal(new Version(2, 0, 1), UpdateChecker.ParseVersion("v2.0.1"));
        Assert.Equal(new Version(2, 1, 0), UpdateChecker.ParseVersion("2.1"));
        Assert.Equal(new Version(3, 0, 0), UpdateChecker.ParseVersion("V3.0.0-beta"));
        Assert.True(UpdateChecker.IsNewer(new Version(2, 0, 1), new Version(2, 0, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(2, 0), new Version(2, 0, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(new Version(1, 9, 9), new Version(2, 0, 0)));
    }

    [Fact]
    public async Task QueriesTheLatestReleaseOfTheRepository()
    {
        var gh = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleaseJson("v2.0.5", new byte[4])) });
        var latest = await new UpdateChecker(new HttpClient(gh)).GetLatestAsync(CancellationToken.None);
        Assert.Equal(new Version(2, 0, 5), latest!.Version);
        Assert.Equal("https://api.github.com/repos/Beco987/Guild-Wars-2-Multiplayer-Music-Player/releases/latest", gh.Requests[0].RequestUri!.ToString());
        Assert.NotEmpty(gh.Requests[0].Headers.UserAgent);

        var none = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await new UpdateChecker(new HttpClient(none)).GetLatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadIsVerifiedAgainstSizeAndChecksum()
    {
        var payload = Encoding.ASCII.GetBytes("new executable");
        var served = payload;
        var gh = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(served) });
        var checker = new UpdateChecker(new HttpClient(gh));
        var asset = UpdateChecker.Parse(ReleaseJson("v2.1.0", payload))!.Asset("GW2MM-Host.exe")!;
        var dest = Path.Combine(_dir, "GW2MM-Host.exe.download");

        await checker.DownloadAsync(asset, dest, CancellationToken.None);
        Assert.Equal(payload, File.ReadAllBytes(dest));

        File.Delete(dest);
        served = Encoding.ASCII.GetBytes("tampered  file"); // same length, different content
        await Assert.ThrowsAsync<InvalidDataException>(() => checker.DownloadAsync(asset, dest, CancellationToken.None));
        Assert.False(File.Exists(dest));
        Assert.Empty(Directory.GetFiles(_dir));

        served = new byte[3];
        await Assert.ThrowsAsync<InvalidDataException>(() => checker.DownloadAsync(asset, dest, CancellationToken.None));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void SwapReplacesTheExecutableAndCleanUpRemovesTheOldOne()
    {
        var exe = Path.Combine(_dir, "GW2MM-Host.exe");
        File.WriteAllText(exe, "old");
        File.WriteAllText(SelfUpdater.DownloadPath(exe), "new");
        SelfUpdater.SwapExecutable(exe, SelfUpdater.DownloadPath(exe));
        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("old", File.ReadAllText(SelfUpdater.OldPath(exe)));
        SelfUpdater.CleanUp(exe);
        Assert.Equal(new[] { exe }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void SwapRestoresTheOldExecutableWhenTheNewFileIsMissing()
    {
        var exe = Path.Combine(_dir, "GW2MM-Client.exe");
        File.WriteAllText(exe, "old");
        Assert.ThrowsAny<IOException>(() => SelfUpdater.SwapExecutable(exe, Path.Combine(_dir, "missing")));
        Assert.Equal("old", File.ReadAllText(exe));
    }

    [Fact]
    public void UpdateCheckSettingDefaultsToOnAndPersists()
    {
        Assert.True(AppSettings.Load(_dir).CheckForUpdates);
        File.WriteAllText(Path.Combine(_dir, "Config.txt"), "OctaveDelay,130\r\nCheckForUpdates,0\r\n");
        var s = AppSettings.Load(_dir);
        Assert.False(s.CheckForUpdates);
        s.CheckForUpdates = true;
        s.SaveDisplaySettings(_dir);
        Assert.True(AppSettings.Load(_dir).CheckForUpdates);
        Assert.Contains("CheckForUpdates,1\n", File.ReadAllText(Path.Combine(_dir, "Config.txt")));
    }
}
