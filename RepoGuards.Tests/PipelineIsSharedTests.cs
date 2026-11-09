using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// Index's and Folio's background pipeline engine lives once, in Storyworthtelling.Pipeline (audit 01 §7).
/// The two were near-verbatim copies with no parity guard, so a fix to one never reached the other.
/// </summary>
public partial class PipelineIsSharedTests
{
    [Fact]
    public void NoAppKeepsItsOwnCopy()
    {
        var copies = Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "Storyworthtelling.Pipeline"))
            .Where(path => !path.EndsWith("PipelineIsSharedTests.cs", StringComparison.Ordinal))
            .Where(path => Copy().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(GuardScan.RepoRoot(), path))
            .ToList();

        Assert.True(copies.Count == 0, "Use Storyworthtelling.Pipeline instead of a copy:\n" + string.Join("\n", copies));
    }

    [Fact]
    public void TheGuardSeesACopy() => Assert.Matches(Copy(), "public sealed class Backoff" + "Scheduler");

    [GeneratedRegex(@"\b(class|record|interface|enum) (PipelineRunner|RunBudget|IPipelineStep|ProcessResult|BackoffScheduler|RateLimitException|ErrorText|MemoryFailure)\b(?!\w)")]
    private static partial Regex Copy();
}
