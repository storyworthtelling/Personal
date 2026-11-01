using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Plan §4.1: a screen the tap watch is not attached to has no dead taps, because nothing is looking. Every
/// Activity in a head on the log loop — and the shared ones in Codex.Ui.Android those heads open — calls
/// <c>TapWatch.Attach(this…)</c>, itself or through a base activity in the same head that does.
/// </summary>
public partial class EveryActivityIsWatchedTests
{
    private static readonly string[] ActivityBases =
        ["Activity", "AppCompatActivity", "ComponentActivity", "FragmentActivity", "Android.App.Activity"];

    public static TheoryData<string> Folders()
    {
        var folders = LogLoopHeads.Folders();
        folders.Add(LogLoopHeads.SharedKit);
        return folders;
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryActivityAttachesTheWatch(string folder)
    {
        var declared = new Dictionary<string, (string Base, string Path, string Text)>(StringComparer.Ordinal);
        foreach (var (path, text) in LogLoopSource.Sources(folder))
        {
            foreach (Match match in ClassDeclaration().Matches(text))
            {
                declared[match.Groups["name"].Value] = (match.Groups["base"].Value, path, text);
            }
        }

        bool IsActivity(string name, int depth) =>
            depth < 10 && declared.TryGetValue(name, out var c)
            && (ActivityBases.Contains(c.Base) || IsActivity(c.Base, depth + 1));

        bool IsWatched(string name, int depth) =>
            depth < 10 && declared.TryGetValue(name, out var c)
            && (c.Text.Contains("TapWatch.Attach(this", StringComparison.Ordinal) || IsWatched(c.Base, depth + 1));

        var activities = declared.Keys.Where(n => IsActivity(n, 0)).ToList();
        Assert.NotEmpty(activities);
        var unwatched = activities.Where(a => !IsWatched(a, 0)).Select(a => $"{a} ({declared[a].Path})").ToList();
        Assert.True(unwatched.Count == 0, "Not watched, so a dead button there is never seen:\n" + string.Join("\n", unwatched));
    }

    [GeneratedRegex(@"class\s+(?<name>\w+)\s*(?:\([^)]*\))?\s*:\s*(?<base>[\w.]+)")]
    private static partial Regex ClassDeclaration();
}
