using Codex.Infrastructure.Updates;
using Xunit;

namespace Codex.Tests.Updates;

public class GitHubReleaseClientTests
{
    private const string LatestUrl = "https://api.github.com/repos/storyworthtelling/Personal/releases/latest";

    // browser_download_url is deliberately the public github.com URL — the fixture proves the
    // client does NOT use it (a token-authenticated GET of it 404s on a private repo) and instead
    // derives the authenticated asset API URL from the repo + asset id.
    private const string SampleRelease = """
        {
          "tag_name": "focusphone-v1.4.0",
          "name": "FocusPhone 1.4.0",
          "assets": [
            { "id": 11, "name": "Flyleaf.apk", "browser_download_url": "https://github.com/storyworthtelling/Personal/releases/download/apps-latest/Flyleaf.apk" },
            { "id": 22, "name": "Post.apk", "browser_download_url": "https://github.com/storyworthtelling/Personal/releases/download/apps-latest/Post.apk" },
            { "id": 33, "name": "checksums.txt", "browser_download_url": "https://github.com/storyworthtelling/Personal/releases/download/apps-latest/checksums.txt" }
          ]
        }
        """;

    private static GitHubReleaseClient NewClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), "storyworthtelling/Personal", "token123");

    [Fact]
    public async Task GetLatestRelease_ParsesTagAndApkAssets()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);

        var release = await NewClient(handler).GetLatestReleaseAsync();

        Assert.Equal("focusphone-v1.4.0", release.TagName);
        Assert.Equal(2, release.ApkAssets.Count);
        Assert.Contains(release.ApkAssets,
            a => a.Name == "Flyleaf.apk" && a.Id == 11);
    }

    [Fact]
    public async Task GetLatestRelease_DownloadUrlIsAuthenticatedAssetApiUrl()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);

        var release = await NewClient(handler).GetLatestReleaseAsync();

        // Must be the api.github.com asset endpoint (built from repo + id), not the public
        // github.com browser_download_url, or the download 404s against the private repo.
        Assert.Contains(release.ApkAssets,
            a => a.Name == "Flyleaf.apk"
                && a.DownloadUrl == "https://api.github.com/repos/storyworthtelling/Personal/releases/assets/11");
    }

    [Fact]
    public async Task GetLatestRelease_ExcludesNonApkAssets()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);

        var release = await NewClient(handler).GetLatestReleaseAsync();

        Assert.DoesNotContain(release.ApkAssets, a => a.Name == "checksums.txt");
    }

    [Fact]
    public async Task GetLatestRelease_SendsAuthAndApiHeaders()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);

        await NewClient(handler).GetLatestReleaseAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("token123", request.Headers.Authorization?.Parameter);
        Assert.True(request.Headers.UserAgent.Count > 0);
    }

    [Fact]
    public async Task GetLatestRelease_HttpFailure_ThrowsWithRepoInMessage()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new GitHubReleaseClient(new HttpClient(handler), "storyworthtelling/Other", "t");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestReleaseAsync());
        Assert.Contains("storyworthtelling/Other", ex.Message);
    }

    // Efficiency audit §13: each APK carries a sidecar naming its package and version, read before any APK is
    // downloaded.
    [Fact]
    public async Task EachApksSidecarIsFoundAndRead()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, """
            {"tag_name":"apps-latest","assets":[
              {"id":11,"name":"Flyleaf-Signed.apk"},
              {"id":12,"name":"Flyleaf-Signed.apk.json"}]}
            """);
        handler.Respond("https://api.github.com/repos/storyworthtelling/Personal/releases/assets/12",
            """{"package":"com.storyworthtelling.focusphone.launcher","versionCode":100321}""");
        var client = NewClient(handler);

        var release = await client.GetLatestReleaseAsync();
        var apk = Assert.Single(release.ApkAssets);
        var candidate = await client.ReadSidecarAsync(release, apk);

        Assert.Equal(new Storyworthtelling.Updates.InstallCandidate("com.storyworthtelling.focusphone.launcher", 100321), candidate);
    }

    [Fact]
    public async Task AnApkWithNoSidecarHasNoCandidate()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);
        var client = NewClient(handler);
        var release = await client.GetLatestReleaseAsync();

        Assert.Null(await client.ReadSidecarAsync(release, release.ApkAssets[0]));
    }
}
