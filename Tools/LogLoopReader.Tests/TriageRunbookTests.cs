namespace LogLoopReader.Tests;

// The runbook is what the scheduled session follows, so the rule that triage must finish lives there as text a test can read.
public class TriageRunbookTests
{
    [Fact]
    public void TheRunbookRefusesToReportOrTrashUntilTriageCheckPasses()
    {
        var runbook = File.ReadAllText(Path.Combine(Support.RepoRoot(), ".claude", "log-loop", "RUNBOOK.md"));

        Assert.Contains("triage-check", runbook, StringComparison.Ordinal);
        Assert.Contains("triage-ledger.txt", runbook, StringComparison.Ordinal);
        Assert.Contains("untriaged: 0", runbook, StringComparison.Ordinal);
        Assert.Contains("one subagent per file", runbook, StringComparison.Ordinal);
        Assert.Contains("LogLoopReader -- backlog", runbook, StringComparison.Ordinal);
        Assert.Contains("never trash", runbook.ToLowerInvariant(), StringComparison.Ordinal);
    }
}
