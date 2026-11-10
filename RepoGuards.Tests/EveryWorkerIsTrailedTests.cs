using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Every WorkManager worker in every head, native and MAUI, runs inside the worker trail and says when it was stopped
/// (audit 08 §18, §20). Index's worksheet worker had no trail at all because the old guard covered only the native heads,
/// and no worker overrode OnStopped, so a run killed at the cap left a lone "started" line.
/// </summary>
public class EveryWorkerIsTrailedTests
{
    private static IEnumerable<(string Relative, string Text)> Workers() =>
        Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(path => (Relative: Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/'), Text: File.ReadAllText(path)))
            .Where(file => Regex.IsMatch(file.Text, @"override\s+Result\s+DoWork\(\)"));

    [Fact]
    public void ThereAreWorkersToCheck() => Assert.True(Workers().Count() >= 15);

    [Fact]
    public void EveryWorkerRunsInsideTheTrail()
    {
        var bare = Workers()
            .Where(file => !Regex.IsMatch(file.Text, @"override\s+Result\s+DoWork\(\)\s*=>\s*\w+\.Worker\(nameof\("))
            .Select(file => file.Relative)
            .ToList();

        Assert.True(bare.Count == 0, "Wrap DoWork in HeadDiagnostics.Worker / MauiLogLoop.Worker:\n" + string.Join("\n", bare));
    }

    [Fact]
    public void EveryWorkerSaysWhenItWasStopped()
    {
        var silent = Workers()
            .Where(file => !Regex.IsMatch(file.Text, @"override\s+void\s+OnStopped\(\)")
                || !Regex.IsMatch(file.Text, @"\w+\.Stopped\(nameof\("))
            .Select(file => file.Relative)
            .ToList();

        Assert.True(silent.Count == 0, "Override OnStopped with HeadDiagnostics.Stopped / MauiLogLoop.Stopped:\n" + string.Join("\n", silent));
    }
}
