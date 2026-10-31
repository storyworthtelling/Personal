using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Plan §4.1: a screen the tap watch is not attached to has no dead taps, because nothing is looking.
/// Every Activity in a head on the log loop — and the shared ones in Codex.Ui.Android those heads open —
/// calls <c>TapWatch.Attach(this…)</c>, and each such head installs the watch. Post only for Phase 1;
/// Phase 2 adds Flyleaf, Score, Plate, Pulp and Constitution to <see cref="Heads"/>.
/// </summary>
public partial class EveryActivityIsWatchedTests
{
    private static readonly string[] Heads = ["FocusPhone/Post.Android"];

    private const string Shared = "FocusPhone/Codex.Ui.Android";

    public static TheoryData<string> Folders() => [.. Heads, Shared];

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryActivityAttachesTheWatch(string folder)
    {
        var activities = LogLoopSource.Sources(folder)
            .SelectMany(s => ActivityDeclaration().Matches(s.Text).Select(m => (s.Path, Name: m.Groups["name"].Value, s.Text)))
            .ToList();

        Assert.NotEmpty(activities);
        var unwatched = activities
            .Where(a => !a.Text.Contains("TapWatch.Attach(this", StringComparison.Ordinal))
            .Select(a => $"{a.Name} ({a.Path})")
            .ToList();
        Assert.True(unwatched.Count == 0, "Not watched, so a dead button there is never seen:\n" + string.Join("\n", unwatched));
    }

    [Theory]
    [MemberData(nameof(HeadFolders))]
    public void EveryHeadOnTheLoopInstallsTheWatch(string folder)
    {
        var installs = LogLoopSource.Sources(folder).Where(s => s.Text.Contains("TapWatch.Install(", StringComparison.Ordinal)).ToList();

        Assert.Single(installs);
        Assert.Contains(": Application", installs[0].Text, StringComparison.Ordinal);
    }

    public static TheoryData<string> HeadFolders() => [.. Heads];

    [GeneratedRegex(@"class\s+(?<name>\w+)\s*(?:\([^)]*\))?\s*:\s*(?:Android\.App\.)?Activity\b")]
    private static partial Regex ActivityDeclaration();
}
