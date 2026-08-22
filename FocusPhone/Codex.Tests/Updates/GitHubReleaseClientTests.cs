using Codex.Infrastructure.Updates;
using Xunit;

namespace Codex.Tests.Updates;

public class GitHubReleaseClientTests
{
    private const string LatestUrl = "https://api.github.com/repos/storyworthtelling/Personal/releases/latest";

    private const string SampleRelease = """
        {
          "tag_name": "focusphone-v1.4.0",
          "name": "FocusPhone 1.4.0",
          "assets": [
            { "name": "Flyleaf.apk", "browser_download_url": "https://api.github.com/assets/1" },
            { "name": "FocusPhone.Communication.apk", "browser_download_url": "https://api.github.com/assets/2" },
            { "name": "checksums.txt", "browser_download_url": "https://api.github.com/assets/3" }
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
            a => a.Name == "Flyleaf.apk" && a.DownloadUrl == "https://api.github.com/assets/1");
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

    [Theory]
    [InlineData("focusphone-v1.4.0", "1.3.9", true)]
    [InlineData("focusphone-v1.4.0", "1.4.0", false)]
    [InlineData("focusphone-v1.4.0", "1.5.0", false)]
    [InlineData("v2.0.0", "1.9.9", true)]
    public void IsNewerThan_ComparesSemanticVersions(string tag, string installed, bool expected)
    {
        var release = new ReleaseInfo(tag, []);

        Assert.Equal(expected, release.IsNewerThan(installed));
    }

    [Fact]
    public async Task GetLatestRelease_HttpFailure_ThrowsWithRepoInMessage()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new GitHubReleaseClient(new HttpClient(handler), "storyworthtelling/Other", "t");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestReleaseAsync());
        Assert.Contains("storyworthtelling/Other", ex.Message);
    }
}
