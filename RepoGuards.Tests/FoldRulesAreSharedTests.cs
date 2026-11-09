using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// The dog-ear's rules (DESIGN.md §7, §13) live once, in Storyworthtelling.Folds (audit 01 §16): which
/// notice the corner wears, the order the Waiting page lists, the fold's geometry, the undo decision and
/// its window. They were copied between Codex.Domain and the MAUI kit under parity tests, and the 6 s
/// window was declared five times.
/// </summary>
public partial class FoldRulesAreSharedTests
{
    [Fact]
    public void NoAppKeepsItsOwnCopyOfTheFoldRules()
    {
        var copies = Sources()
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Storyworthtelling.Folds{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => Copy().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .ToList();

        Assert.True(copies.Count == 0, "Use Storyworthtelling.Folds instead of a copy:\n" + string.Join("\n", copies));
    }

    [Fact]
    public void TheUndoWindowIsWrittenOnce()
    {
        var literals = Sources()
            .Where(path => WindowLiteral().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .ToList();

        Assert.Equal(["Storyworthtelling.Folds/FoldTiming.cs"], literals);
    }

    /// <summary>
    /// <c>Task.Delay(...).ContinueWith(async ...)</c> returns an unobserved <c>Task&lt;Task&gt;</c>, so an exception
    /// in the deferred work (Folio's hard delete after the undo window) vanished without a trace.
    /// </summary>
    [Fact]
    public void NoDeferredWorkIsFireAndForget()
    {
        var offenders = Sources()
            .Where(path => FireAndForget().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "Deferred work whose failure nobody sees:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheGuardsSeeWhatTheyLookFor()
    {
        Assert.Matches(Copy(), "public sealed class Notice" + "Order");
        Assert.Matches(Copy(), "public static class DogEar" + "Geometry\n");
        Assert.DoesNotMatch(Copy(), "public class DogEarStateTests");
        Assert.Matches(WindowLiteral(), "public const int UndoWindowMs = " + "6_000;");
        Assert.Matches(FireAndForget(), "_ = Task.Delay(window).ContinueWith(async _ =>");
    }

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(GuardScan.RepoRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !path.EndsWith("FoldRulesAreSharedTests.cs", StringComparison.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');

    [GeneratedRegex(@"\b(class|enum|record) (NoticeOrder|NoticeSeriousness|DogEarGeometry|DogEarState|FoldRole|DogEarHue)\b(?!\w)")]
    private static partial Regex Copy();

    [GeneratedRegex(@"UndoWindowMs\s*=\s*6[_0-9]*\s*;")]
    private static partial Regex WindowLiteral();

    [GeneratedRegex(@"Task\.Delay\([^;]*\)\s*\.ContinueWith\(\s*async")]
    private static partial Regex FireAndForget();
}
