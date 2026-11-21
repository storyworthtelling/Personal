using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// P14 release A fix round, finding 1. Watch APKs are published to a release of their own, which no phone reads, because
/// every Flyleaf and Score already on a phone installs whatever <c>.apk</c> its release carries by package. These hold
/// the phone's end: the phone half of each update service reads the phone's release by its tag and nothing else, only
/// Flyleaf's watch half names the watch's release, and Score never downloads an asset named as a watch APK.
/// </summary>
public class ReleaseChannelGuardTests
{
    private static readonly string[] PhoneUpdaters =
    [
        Path.Combine("FocusPhone", "Flyleaf.Android", "UpdateService.cs"),
        Path.Combine("FocusPhone", "Score.Android", "ScoreUpdateService.cs"),
    ];

    private static readonly string WatchHalf = Path.Combine("FocusPhone", "Flyleaf.Android", "WatchUpdates.cs");

    [Fact]
    public void ThePhoneReadsOnlyThePhonesRelease()
    {
        foreach (var file in PhoneUpdaters)
        {
            var source = Code(file);
            Assert.True(source.Contains("GetReleaseAsync(ReleaseChannels.Phone)", StringComparison.Ordinal),
                $"{file} doesn't read the phone's release by its tag.");
            Assert.DoesNotContain("ReleaseChannels.Watch", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetLatestReleaseAsync", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OnlyTheWatchHalfNamesTheWatchsRelease()
    {
        var naming = Sources()
            .Where(path => Code(path).Contains("ReleaseChannels.Watch", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepoRoot(), path))
            .ToList();

        Assert.Equal([WatchHalf], naming);
        var watch = Code(WatchHalf);
        Assert.Contains("GetReleaseAsync(ReleaseChannels.Watch)", watch, StringComparison.Ordinal);
        Assert.DoesNotContain("ReleaseChannels.Phone", watch, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingAsksForTheRepositorysLatestRelease()
    {
        // releases/latest is whichever release GitHub calls latest; a second release made latest by mistake would be
        // installed from. Each release is read by its own tag.
        var asking = Sources()
            .Where(path => File.ReadAllText(path).Contains("releases/latest", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepoRoot(), path))
            .ToList();

        Assert.True(asking.Count == 0, "Reads releases/latest:\n" + string.Join("\n", asking));
    }

    [Fact]
    public void ScoreSkipsAWatchAssetBeforeDownloadingIt()
    {
        // Score downloads each asset it considers in full, and Wave carries Score's package: an asset named as a watch
        // APK is passed over by its name, before a byte is fetched.
        var source = Code(Path.Combine("FocusPhone", "Score.Android", "ScoreUpdateService.cs"));
        var skip = Regex.Match(source, @"ApkForms\.FromAssetName\(asset\.Name\)\s*!=\s*ApkForm\.Phone");
        var download = source.IndexOf("await DownloadAsync(", StringComparison.Ordinal);

        Assert.True(skip.Success, "Score doesn't pass over a watch asset by its name.");
        Assert.True(download > skip.Index, "Score downloads an asset before asking its name's form.");
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "FocusPhone"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Codex.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    // A source without its comments, so a comment naming a release is not a reader of it. (A URL's "//" goes too, so a
    // URL is read from the raw text.)
    private static string Code(string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(RepoRoot(), path);
        Assert.True(File.Exists(full), $"{path} isn't there.");
        return Regex.Replace(File.ReadAllText(full), @"///.*|//.*", string.Empty);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

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
