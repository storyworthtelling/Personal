using Codex.Domain.Web;
using Xunit;

namespace Codex.Tests.Web;

public class DownloadHeuristicsTests
{
    private static Uri U(string url) => new(url);

    // The reported case: a GitHub Actions artifact link redirects github.com to a
    // signed Azure blob URL whose path is an opaque hash and whose Content-Disposition
    // rides in the "rscd" query parameter.
    private const string GitHubArtifactUrl =
        "https://productionresultssa19.blob.core.windows.net/actions-results/"
        + "7df1c041/artifacts/5f9899e401425a27f36a9e1205bd5db356e070996ce90adc5c03fcc126a99b35.zip"
        + "?rscd=attachment%3B+filename%3D%22FocusPhone-Android.zip%22&rsct=application%2Fzip&sig=abc";

    [Fact]
    public void GitHubArtifactBlobUrl_IsDownload()
    {
        Assert.True(DownloadHeuristics.IsDownloadUrl(U(GitHubArtifactUrl)));
    }

    [Fact]
    public void GitHubArtifactBlobUrl_RecoversHumanFilename()
    {
        var disposition = DownloadHeuristics.ReadContentDisposition(U(GitHubArtifactUrl));
        Assert.Equal("attachment; filename=\"FocusPhone-Android.zip\"", disposition);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo/releases/download/v1.0/focusphone.apk")]
    [InlineData("https://cdn.example.org/builds/app-release.apk")]
    [InlineData("https://files.example.net/report.pdf")]
    [InlineData("https://files.example.net/archive.zip")]
    public void FileExtensionOnPath_IsDownload(string url)
    {
        Assert.True(DownloadHeuristics.IsDownloadUrl(U(url)));
    }

    [Fact]
    public void ResponseContentDispositionAttachment_IsDownload()
    {
        // S3/GCS style: opaque path, disposition spelled out in the query.
        Assert.True(DownloadHeuristics.IsDownloadUrl(U(
            "https://bucket.s3.amazonaws.com/9f8e7d?response-content-disposition=attachment")));
    }

    [Theory]
    [InlineData("https://other.com/about")]
    [InlineData("https://other.com/docs/index.html")]
    [InlineData("https://other.com/page.aspx")]
    [InlineData("https://other.com/")]
    public void PageLink_IsNotDownload(string url)
    {
        Assert.False(DownloadHeuristics.IsDownloadUrl(U(url)));
    }

    [Fact]
    public void PurelyNumericSuffix_IsNotDownload()
    {
        // A version-like trailing segment is not a file extension.
        Assert.False(DownloadHeuristics.IsDownloadUrl(U("https://other.com/release/1.2024")));
    }

    [Theory]
    [InlineData("intent://scan/#Intent;scheme=zxing;end")]
    [InlineData("market://details?id=com.example")]
    [InlineData("file:///sdcard/app.apk")]
    public void NonWebScheme_IsNotDownload(string url)
    {
        Assert.False(DownloadHeuristics.IsDownloadUrl(U(url)));
    }
}
