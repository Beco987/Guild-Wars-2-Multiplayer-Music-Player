using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace GW2MM.Core.Updates;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Sha256);

public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string fileName) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, fileName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Looks up the latest published GitHub release of GW2MM and downloads its executables. Drafts and pre-releases
/// are never offered (the "latest release" endpoint excludes them).
/// </summary>
public sealed class UpdateChecker
{
    public const string Repository = "Beco987/Guild-Wars-2-Multiplayer-Music-Player";

    private readonly HttpClient _http;
    private readonly string _repository;

    public UpdateChecker(HttpClient http, string repository = Repository)
    {
        _http = http;
        _repository = repository;
    }

    /// <summary>The latest release, or null when the repository has none or its tag is not a version.</summary>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GW2MM", CurrentVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;
        if (ParseVersion(tag) is not { } version) return null;
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in list.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                var size = a.TryGetProperty("size", out var s) ? s.GetInt64() : -1;
                string? sha = null;
                if (a.TryGetProperty("digest", out var dg) && dg.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = digest[7..].ToLowerInvariant();
                assets.Add(new ReleaseAsset(name, url, size, sha));
            }
        }
        return new ReleaseInfo(version, tag, page, assets);
    }

    /// <summary>"v2.1.0" / "2.1" -> 2.1.0; null when the tag is not a version.</summary>
    public static Version? ParseVersion(string tag)
    {
        var s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var dash = s.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) s = s[..dash];
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>The version of the running application (from the entry assembly).</summary>
    public static Version CurrentVersion =>
        Normalize(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// Downloads <paramref name="asset"/> to <paramref name="destination"/> and checks it against the release's
    /// size and SHA-256 digest (when GitHub provides one). A file that fails a check is deleted.
    /// </summary>
    public async Task DownloadAsync(ReleaseAsset asset, string destination, CancellationToken ct)
    {
        var tmp = destination + ".part";
        try
        {
            using (var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var file = File.Create(tmp);
                await source.CopyToAsync(file, ct).ConfigureAwait(false);
            }
            var info = new FileInfo(tmp);
            if (asset.Size >= 0 && info.Length != asset.Size)
                throw new InvalidDataException($"The download is {info.Length} bytes, expected {asset.Size}.");
            if (asset.Sha256 is { } expected)
            {
                string actual;
                await using (var f = File.OpenRead(tmp))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct).ConfigureAwait(false)).ToLowerInvariant();
                if (actual != expected) throw new InvalidDataException("The download does not match the release's checksum.");
            }
            File.Move(tmp, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}

/// <summary>
/// Replaces the running executable. Windows does not allow overwriting a running .exe but does allow renaming it,
/// so the current file becomes "&lt;name&gt;.old" (deleted on the next start) and the new file takes its place.
/// </summary>
public static class SelfUpdater
{
    public static string OldPath(string exePath) => exePath + ".old";
    public static string DownloadPath(string exePath) => exePath + ".download";

    public static void SwapExecutable(string exePath, string newFile)
    {
        var old = OldPath(exePath);
        if (File.Exists(old)) File.Delete(old);
        File.Move(exePath, old);
        try { File.Move(newFile, exePath); }
        catch
        {
            File.Move(old, exePath); // put the working version back
            throw;
        }
    }

    /// <summary>Removes what a previous update left behind (best effort: the old process may still be exiting).</summary>
    public static void CleanUp(string exePath)
    {
        foreach (var path in new[] { OldPath(exePath), DownloadPath(exePath), DownloadPath(exePath) + ".part" })
        {
            for (var attempt = 0; attempt < 10 && File.Exists(path); attempt++)
            {
                try { File.Delete(path); }
                catch (IOException) { Thread.Sleep(300); }
                catch (UnauthorizedAccessException) { Thread.Sleep(300); }
            }
        }
    }
}
