using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// The suite's time words live once, in Storyworthtelling.Time (audit 01 §13). There were six copies held
/// together by a parity test that covered four; Index's had already drifted, and Pulp printed "5 min ago"
/// where every other app says "5m ago".
/// </summary>
public partial class TimeWordsAreSharedTests
{
    [Fact]
    public void NoAppKeepsItsOwnCopy()
    {
        var copies = Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "Storyworthtelling.Time"))
            .Where(path => Copy().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(GuardScan.RepoRoot(), path))
            .ToList();

        Assert.True(copies.Count == 0, "Use Storyworthtelling.Time instead of a copy:\n" + string.Join("\n", copies));
    }

    [Fact]
    public void TheGuardSeesACopy()
    {
        Assert.Matches(Copy(), "public static class Relative" + "Time\n{");
        Assert.Matches(Copy(), "internal static class Time" + "Formats");
        Assert.DoesNotMatch(Copy(), "public class RelativeTimeTests");
    }

    [GeneratedRegex(@"\bclass (RelativeTime|TimeFormats)\b(?!\w)")]
    private static partial Regex Copy();
}
