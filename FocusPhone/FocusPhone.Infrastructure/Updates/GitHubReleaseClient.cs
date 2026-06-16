using System.Text.Json;
using System.Text.RegularExpressions;

namespace FocusPhone.Infrastructure.Updates;

public record ReleaseAsset(string Name, string DownloadUrl);

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
            throw new HttpRequestException(
                $"Fetching the latest release of {repo} failed with {(int)response.StatusCode} {response.StatusCode}.");
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var apkAssets = root.GetProperty("assets").EnumerateArray()
            .Select(asset => new ReleaseAsset(
                asset.GetProperty("name").GetString()!,
                asset.GetProperty("browser_download_url").GetString()!))
            .Where(asset => asset.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new ReleaseInfo(root.GetProperty("tag_name").GetString()!, apkAssets);
    }
}
