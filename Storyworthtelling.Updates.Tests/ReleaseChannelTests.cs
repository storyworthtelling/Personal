using System.Text.RegularExpressions;

namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// P14 release A fix round, finding 1: every Flyleaf and Score already on a phone lists its release's assets by
/// <c>.apk</c> and installs by package, whatever the form. A watch APK on the phone's release would replace Flyleaf on
/// the locked phone for good, and the face would land on both phones. So watch APKs live on a release of their own, which
/// nothing on a phone has ever read, and each form's release is one frozen word.
/// </summary>
public class ReleaseChannelTests
{
    [Fact]
    public void TheChannelsAreFrozen()
    {
        Assert.Equal("apps-latest", ReleaseChannels.Phone);
        Assert.Equal("watch-latest", ReleaseChannels.Watch);

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Storyworthtelling.Updates", "ReleaseChannels.cs"));
        var code = Regex.Replace(source, @"///.*|//.*", string.Empty);
        Assert.DoesNotContain("nameof(", code, StringComparison.Ordinal);
        Assert.All(Regex.Matches(code, @"public const string (\w+) = ([^;]+);").Select(match => match.Groups[2].Value.Trim()),
            value => Assert.Matches(@"^""[^""]+""$", value));
    }

    [Fact]
    public void EachFormHasItsOwnRelease()
    {
        Assert.Equal(ReleaseChannels.Phone, ReleaseChannels.For(ApkForm.Phone));
        Assert.Equal(ReleaseChannels.Watch, ReleaseChannels.For(ApkForm.Watch));
        Assert.NotEqual(ReleaseChannels.Phone, ReleaseChannels.Watch);
    }

    [Fact]
    public void AnUnknownFormHasNoRelease()
    {
        Assert.Null(ReleaseChannels.For(ApkForm.Unknown));
    }

    private static string RepoRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md")))
        {
            root = Path.GetDirectoryName(root);
        }
        return root!;
    }
}
