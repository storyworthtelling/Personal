using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codex.Infrastructure.Updates;

// Id is the GitHub asset id: stable per uploaded blob and re-minted whenever CI
// replaces the file, so the update worker uses it to skip re-downloading an
// unchanged APK (data-frugal on a phone) while still catching a fresh build.
// Defaulted so callers/tests that don't supply it keep compiling.
//
// DownloadUrl is the authenticated asset API endpoint
// (https://api.github.com/repos/{repo}/releases/assets/{id}), NOT the public
// browser_download_url: the repo is private, so a token-authenticated GET of the
// browser URL returns 404. The asset API endpoint, fetched with a Bearer token and
// Accept: application/octet-stream, redirects to the signed blob and downloads.
public record ReleaseAsset(string Name, string DownloadUrl, long Id = 0);

public record ReleaseInfo(string TagName, IReadOnlyList<ReleaseAsset> ApkAssets)
{
    /// <summary>The release's sidecars (<c>&lt;apk&gt;.json</c>), by name (efficiency audit §13).</summary>
    public IReadOnlyDictionary<string, ReleaseAsset> Sidecars { get; init; } = new Dictionary<string, ReleaseAsset>();

    public bool IsNewerThan(string installedVersion)
    {
        var match = Regex.Match(TagName, @"\d+\.\d+\.\d+");
        return match.Success && Version.Parse(match.Value) > Version.Parse(installedVersion);
    }
}

/// <summary>
/// The private update channel: reads the latest GitHub Release of this repo so
/// the launcher's UpdateService can silent-install newer signed APKs.
/// </summary>
public class GitHubReleaseClient
{
    private readonly HttpClient http;
    private readonly string repo;
    private readonly string token;

    public GitHubReleaseClient(HttpClient http, string repo, string token)
    {
        this.http = http;
        this.repo = repo;
        this.token = token;
    }

    public async Task<ReleaseInfo> GetLatestReleaseAsync()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("FocusPhone-UpdateService");

        var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            // Carry the status code on the exception so the caller can tell a rejected token (401/403) from a
            // transient failure and record the update token's health accordingly.
            throw new HttpRequestException(
                $"Fetching the latest release of {repo} failed with {(int)response.StatusCode} {response.StatusCode}.",
                inner: null, response.StatusCode);
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(asset =>
            {
                var id = asset.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : 0;
                // Download through the authenticated asset API endpoint, not browser_download_url:
                // the private repo returns 404 for a token-authenticated GET of the public URL.
                return new ReleaseAsset(
                    asset.GetProperty("name").GetString()!,
                    $"https://api.github.com/repos/{repo}/releases/assets/{id}",
                    id);
            })
            .ToList();
        var apkAssets = assets.Where(asset => asset.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)).ToList();
        var sidecars = assets
            .Where(asset => asset.Name.EndsWith(".apk.json", StringComparison.OrdinalIgnoreCase))
            .GroupBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return new ReleaseInfo(root.GetProperty("tag_name").GetString()!, apkAssets) { Sidecars = sidecars };
    }

    /// <summary>
    /// The package and versionCode the APK's sidecar names, or null when it has none or it will not read, in which
    /// case the APK is downloaded and read as before (efficiency audit §13). A few hundred bytes against tens of MB.
    /// </summary>
    public async Task<Storyworthtelling.Updates.InstallCandidate?> ReadSidecarAsync(ReleaseInfo release, ReleaseAsset apk)
    {
        if (!release.Sidecars.TryGetValue(Storyworthtelling.Updates.ApkSidecar.NameFor(apk.Name), out var sidecar))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, sidecar.DownloadUrl);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Headers.UserAgent.ParseAdd("FocusPhone-UpdateService");
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode
            ? Storyworthtelling.Updates.ApkSidecar.Read(await response.Content.ReadAsStringAsync())
            : null;
    }
}
