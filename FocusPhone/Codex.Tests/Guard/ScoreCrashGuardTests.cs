using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Two Score crashes that only show on a phone: an offline podcast refresh faulting as an
/// unobserved task, and TIDAL's Player failing to construct for want of the Dagger runtime.
/// Neither can be exercised here, so these read the source as the other guards do.
/// </summary>
public class ScoreCrashGuardTests
{
    [Fact]
    public void FeedFetchFoldsAndroidDnsFailuresIntoAFailedHost()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", "Score.Android", "PodcastsArea.cs"));
        var start = source.IndexOf("FetchFeedAsync(RssFeedClient", StringComparison.Ordinal);

        Assert.True(start >= 0);
        var body = source[start..];
        Assert.Contains("System.Net.WebException", body);
        Assert.Contains("System.IO.IOException", body);
    }

    [Fact]
    public void TidalBindingPackagesTheDaggerRuntimeThePlayerComponentCallsInto()
    {
        var csproj = File.ReadAllText(Path.Combine(
            RepoRoot(), "FocusPhone", "Codex.Tidal.Binding", "Codex.Tidal.Binding.csproj"));

        Assert.Contains("Xamarin.Google.Dagger", csproj);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException(".github not found above " + AppContext.BaseDirectory);
    }
}
