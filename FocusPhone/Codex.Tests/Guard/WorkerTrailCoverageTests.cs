using System.Text.RegularExpressions;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Plan §4.3 (G7): a background worker that silently never runs, or runs and fails, leaves nothing unless
/// it says so. Every Worker in a head on the loop runs its body through the head's worker trail, which
/// logs start, end, result and duration. Post only for Phase 1.
/// </summary>
public partial class WorkerTrailCoverageTests
{
    [Fact]
    public void EveryPostWorkerRunsThroughTheTrail()
    {
        var workers = LogLoopSource.Sources("FocusPhone/Post.Android")
            .SelectMany(s => WorkerDeclaration().Matches(s.Text).Select(m => (s.Path, Name: m.Groups["name"].Value, Body: ClassBody(s.Text, m.Index))))
            .ToList();

        Assert.True(workers.Count >= 2, "Expected at least EmailSyncWorker and LogUploadWorker.");
        var untrailed = workers.Where(w => !w.Body.Contains("PostDiagnostics.Worker(", StringComparison.Ordinal)).Select(w => w.Name).ToList();
        Assert.True(untrailed.Count == 0, "Not on the worker trail: " + string.Join(", ", untrailed));
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
}
