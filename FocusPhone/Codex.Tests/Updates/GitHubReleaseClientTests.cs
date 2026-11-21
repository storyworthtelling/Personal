using Codex.Infrastructure.Updates;
using Xunit;

namespace Codex.Tests.Updates;

public class GitHubReleaseClientTests
{
    // The phone's release by its tag (P14 release A fix round): never releases/latest, which is whichever release GitHub
    // calls latest, and never the watch's.
    private const string LatestUrl = "https://api.github.com/repos/storyworthtelling/Personal/releases/tags/apps-latest";

    private const string WatchUrl = "https://api.github.com/repos/storyworthtelling/Personal/releases/tags/watch-latest";

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
    public async Task GetRelease_ParsesTagAndApkAssets()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);

        var release = await NewClient(handler).GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone);

        Assert.Equal("focusphone-v1.4.0", release.TagName);
        Assert.Equal(2, release.ApkAssets.Count);
        Assert.Contains(release.ApkAssets,
            a => a.Name == "Flyleaf.apk" && a.Id == 11);
    }

    [Fact]
    public async Task GetRelease_DownloadUrlIsAuthenticatedAssetApiUrl()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);

        var release = await NewClient(handler).GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone);

        // Must be the api.github.com asset endpoint (built from repo + id), not the public
        // github.com browser_download_url, or the download 404s against the private repo.
        Assert.Contains(release.ApkAssets,
            a => a.Name == "Flyleaf.apk"
                && a.DownloadUrl == "https://api.github.com/repos/storyworthtelling/Personal/releases/assets/11");
    }

    [Fact]
    public async Task GetRelease_ExcludesNonApkAssets()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);

        var release = await NewClient(handler).GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone);

        Assert.DoesNotContain(release.ApkAssets, a => a.Name == "checksums.txt");
    }

    [Fact]
    public async Task GetRelease_SendsAuthAndApiHeaders()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);

        await NewClient(handler).GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("token123", request.Headers.Authorization?.Parameter);
        Assert.True(request.Headers.UserAgent.Count > 0);
    }

    [Fact]
    public async Task GetRelease_HttpFailure_ThrowsWithRepoInMessage()
    {
        var handler = new FakeHttpMessageHandler();
        var client = new GitHubReleaseClient(new HttpClient(handler), "storyworthtelling/Other", "t");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone));
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

        var release = await client.GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone);
        var apk = Assert.Single(release.ApkAssets);
        var candidate = await client.ReadSidecarAsync(release, apk);

        Assert.Equal(new Storyworthtelling.Updates.InstallCandidate("com.storyworthtelling.focusphone.launcher", 100321, Storyworthtelling.Updates.ApkForm.Phone), candidate);
    }

    // P14 W-3.1.1: Post's APK and Dash share a package, so the release carries both, under names the publish action
    // keeps apart. Each APK's sidecar is its own; the watch one never answers for the phone one.
    [Fact]
    public async Task APhoneAndAWatchApkOfOnePackageKeepTheirOwnSidecars()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, """
            {"tag_name":"apps-latest","assets":[
              {"id":21,"name":"com.storyworthtelling.focusphone.communication-Signed.apk"},
              {"id":22,"name":"com.storyworthtelling.focusphone.communication-Signed.apk.json"},
              {"id":23,"name":"com.storyworthtelling.focusphone.communication-Signed.watch.apk"},
              {"id":24,"name":"com.storyworthtelling.focusphone.communication-Signed.watch.apk.json"}]}
            """);
        handler.Respond("https://api.github.com/repos/storyworthtelling/Personal/releases/assets/22",
            """{"package":"com.storyworthtelling.focusphone.communication","versionCode":103500000,"form":"phone"}""");
        handler.Respond("https://api.github.com/repos/storyworthtelling/Personal/releases/assets/24",
            """{"package":"com.storyworthtelling.focusphone.communication","versionCode":1103500000,"form":"watch"}""");
        var client = NewClient(handler);

        var release = await client.GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone);
        var phone = await client.ReadSidecarAsync(release, release.ApkAssets.Single(a => a.Id == 21));
        var watch = await client.ReadSidecarAsync(release, release.ApkAssets.Single(a => a.Id == 23));

        Assert.Equal(2, release.ApkAssets.Count);
        Assert.Equal(Storyworthtelling.Updates.ApkForm.Phone, phone!.Form);
        Assert.Equal(103500000, phone.VersionCode);
        Assert.Equal(Storyworthtelling.Updates.ApkForm.Watch, watch!.Form);
        Assert.Equal(1103500000, watch.VersionCode);
    }

    [Fact]
    public async Task EachReleaseIsReadByItsOwnTag()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, """{"tag_name":"apps-latest","assets":[{"id":1,"name":"Flyleaf-Signed.apk"}]}""");
        handler.Respond(WatchUrl, """{"tag_name":"watch-latest","assets":[{"id":2,"name":"com.storyworthtelling.focusphone.launcher-Signed.watch.apk"}]}""");
        var client = NewClient(handler);

        var phone = await client.GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone);
        var watch = await client.GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Watch);

        Assert.Equal("apps-latest", phone.TagName);
        Assert.Equal(1, Assert.Single(phone.ApkAssets).Id);
        Assert.Equal("watch-latest", watch.TagName);
        Assert.Equal(2, Assert.Single(watch.ApkAssets).Id);
        Assert.Equal([LatestUrl, WatchUrl], handler.Requests.Select(request => request.RequestUri!.ToString()));
    }

    [Fact]
    public async Task ARepositorysLatestReleaseIsNeverAsked()
    {
        // Whichever release GitHub calls latest is not the phone's question: a second release made latest by mistake
        // would otherwise be installed from.
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);

        await NewClient(handler).GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone);

        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnApkWithNoSidecarHasNoCandidate()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond(LatestUrl, SampleRelease);
        var client = NewClient(handler);
        var release = await client.GetReleaseAsync(Storyworthtelling.Updates.ReleaseChannels.Phone);

        Assert.Null(await client.ReadSidecarAsync(release, release.ApkAssets[0]));
    }
}
