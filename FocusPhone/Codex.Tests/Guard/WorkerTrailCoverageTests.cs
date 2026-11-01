using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Plan §4.3 (G7): a background worker that silently never runs, or runs and fails, leaves nothing unless
/// it says so. Every Worker in a head on the loop, and the shared upload worker, runs its body through the
/// worker trail (start, end, result and duration).
/// </summary>
public partial class WorkerTrailCoverageTests
{
    public static TheoryData<string> Folders()
    {
        var folders = LogLoopHeads.Folders();
        folders.Add(LogLoopHeads.SharedKit);
        return folders;
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryWorkerRunsThroughTheTrail(string folder)
    {
        var workers = LogLoopSource.Sources(folder)
            .SelectMany(s => WorkerDeclaration().Matches(s.Text).Select(m => (s.Path, Name: m.Groups["name"].Value, Body: ClassBody(s.Text, m.Index))))
            .ToList();

        var untrailed = workers.Where(w => !Trailed().IsMatch(w.Body)).Select(w => $"{w.Name} ({w.Path})").ToList();
        Assert.True(untrailed.Count == 0, "Not on the worker trail: " + string.Join(", ", untrailed));
    }

    [Fact]
    public void ThereAreWorkersToCheck()
    {
        var count = LogLoopHeads.All.Sum(h => LogLoopSource.Sources(h.Folder).Sum(s => WorkerDeclaration().Matches(s.Text).Count));

        Assert.True(count >= 6, $"Expected the suite's workers (Post 2, Flyleaf 1, Score 3), found {count}.");
    }

    private static string ClassBody(string text, int start)
    {
        var open = text.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            depth += text[i] == '{' ? 1 : text[i] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return text[open..(i + 1)];
            }
        }
        return text[open..];
    }

    [GeneratedRegex(@"class\s+(?<name>\w+)\s*(?:\([^)]*\))?\s*:\s*(?:AndroidX\.Work\.)?Worker\b")]
    private static partial Regex WorkerDeclaration();

    [GeneratedRegex(@"\b\w*Diagnostics\.Worker\(")]
    private static partial Regex Trailed();
}
