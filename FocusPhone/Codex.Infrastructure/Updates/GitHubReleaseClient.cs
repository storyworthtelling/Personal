using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codex.Infrastructure.Updates;

// Id is the GitHub asset id: stable per uploaded blob and re-minted whenever CI
// replaces the file, so the update worker uses it to skip re-downloading an
// unchanged APK (data-frugal on a phone) while still catching a fresh build.
// Defaulted so callers/tests that don't supply it keep compiling.
public record ReleaseAsset(string Name, string DownloadUrl, long Id = 0);

public record ReleaseInfo(string TagName, IReadOnlyList<ReleaseAsset> ApkAssets)
{
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
        var apkAssets = root.GetProperty("assets").EnumerateArray()
            .Select(asset => new ReleaseAsset(
                asset.GetProperty("name").GetString()!,
                asset.GetProperty("browser_download_url").GetString()!,
                asset.TryGetProperty("id", out var id) ? id.GetInt64() : 0))
            .Where(asset => asset.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new ReleaseInfo(root.GetProperty("tag_name").GetString()!, apkAssets);
    }
}
