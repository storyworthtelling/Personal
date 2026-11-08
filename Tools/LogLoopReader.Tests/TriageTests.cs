using System.Text.Json;
using LogLoopReader;

namespace LogLoopReader.Tests;

// The weekly session once read 234 fingerprints, looked at a fraction and posted a report anyway. Triage is now a
// checklist the reader writes and refuses to call finished while a line is open, and a ledger of fingerprints (never
// log text) carries last week's decisions forward, so a week only asks about what is new or still wrong.
public class TriageTests
{
    private static DigestGroup Group(string fp, string kind = "message", string level = "WARN", int count = 3) =>
        new(fp, "post", kind, "k-" + fp, count, ["owner"], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [level], "sample");

    [Fact]
    public void InfoOnlyMessagesAreFoldedAndNeverAskedAbout()
    {
        var plan = Triage.Plan([Group("a", level: "INFO")], TriageLedger.Empty);

        Assert.Equal(TriageStatus.Folded, Assert.Single(plan).Status);
    }

    [Fact]
    public void AnInfoExceptionOrTapIsNotFolded()
    {
        var plan = Triage.Plan([Group("a", kind: "exception", level: "INFO"), Group("b", kind: "tap", level: "INFO")], TriageLedger.Empty);

        Assert.All(plan, i => Assert.Equal(TriageStatus.Pending, i.Status));
    }

    [Fact]
    public void ANewWarningIsPending()
    {
        Assert.Equal(TriageStatus.Pending, Assert.Single(Triage.Plan([Group("a")], TriageLedger.Empty)).Status);
    }

    [Fact]
    public void AFingerprintDecidedLastWeekAsNoActionIsCarried()
    {
        var ledger = TriageLedger.Empty.With("a", TriageOutcome.NoAction, "C5", "2026-11-01");

        var item = Assert.Single(Triage.Plan([Group("a")], ledger));

        Assert.Equal(TriageStatus.Carried, item.Status);
        Assert.Equal(TriageOutcome.NoAction, item.Outcome);
    }

    [Theory]
    [InlineData(TriageOutcome.Report)]
    [InlineData(TriageOutcome.Fix)]
    public void AReportedOrFixedFingerprintThatIsStillHereIsAskedAgain(TriageOutcome before)
    {
        var ledger = TriageLedger.Empty.With("a", before, "C2", "2026-11-01");

        Assert.Equal(TriageStatus.Pending, Assert.Single(Triage.Plan([Group("a")], ledger)).Status);
    }

    [Fact]
    public void CheckFailsNamingHowManyAreOpen()
    {
        var items = Triage.Plan([Group("a"), Group("b")], TriageLedger.Empty);
        var decided = items.Select(i => i.Fingerprint == "a" ? i with { Outcome = TriageOutcome.Report, Class = "C12" } : i).ToList();

        var result = Triage.Check(decided);

        Assert.False(result.Complete);
        Assert.Equal(["b"], result.Open);
    }

    [Fact]
    public void CheckPassesWhenEveryPendingItemHasAnOutcomeAndAClass()
    {
        var items = Triage.Plan([Group("a")], TriageLedger.Empty)
            .Select(i => i with { Outcome = TriageOutcome.NoAction, Class = "C5" }).ToList();

        Assert.True(Triage.Check(items).Complete);
    }

    [Fact]
    public void AnOutcomeWithoutAClassStillCountsAsOpen()
    {
        var items = Triage.Plan([Group("a")], TriageLedger.Empty)
            .Select(i => i with { Outcome = TriageOutcome.Report }).ToList();

        Assert.False(Triage.Check(items).Complete);
    }

    [Fact]
    public void TheLedgerIsUpdatedFromDecisionsAndRoundTripsAsPlainLines()
    {
        var items = Triage.Plan([Group("a")], TriageLedger.Empty)
            .Select(i => i with { Outcome = TriageOutcome.Fix, Class = "C2" }).ToList();

        var json = Triage.Merge(TriageLedger.Empty, items, "2026-11-08").ToText();

        Assert.Equal(TriageOutcome.Fix, TriageLedger.FromText(json).Get("a")!.Outcome);
        Assert.DoesNotContain("sample", json, StringComparison.Ordinal);
        Assert.DoesNotContain("k-a", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyOrMissingLedgerReadsAsEmpty()
    {
        Assert.Null(TriageLedger.FromText("").Get("a"));
        Assert.Null(TriageLedger.FromText("\n").Get("a"));
    }
}

public class TriageSplitTests
{
    private static TriageItem Item(string fp, string app, TriageStatus status = TriageStatus.Pending, TriageOutcome? outcome = null) =>
        new(fp, app, "message", 1, status, outcome, outcome is null ? null : "C5");

    [Fact]
    public void ItemsAreSplitByAppSoEachSubagentOwnsOneFile()
    {
        var split = Triage.ByApp([Item("a", "post"), Item("b", "score"), Item("c", "post")]);

        Assert.Equal(["a", "c"], split["post"].Select(i => i.Fingerprint));
        Assert.Equal(["b"], split["score"].Select(i => i.Fingerprint));
    }

    [Fact]
    public void AnAppWithNothingToDecideGetsNoFile()
    {
        var split = Triage.ByApp([Item("a", "post", TriageStatus.Folded), Item("b", "score")]);

        Assert.False(split.ContainsKey("post"));
    }

    [Fact]
    public void ReportCountsNameHowManyAreStillOpen()
    {
        var counts = Triage.Counts([Item("a", "post", outcome: TriageOutcome.Fix), Item("b", "post", outcome: TriageOutcome.Report),
            Item("c", "post", outcome: TriageOutcome.NoAction), Item("d", "post"), Item("e", "post", TriageStatus.Folded)]);

        Assert.Equal((1, 1, 1, 1), (counts.Fix, counts.Report, counts.NoAction, counts.Untriaged));
    }
}

public class BacklogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OnlyFilesThatAreNotDoneAreListedWithTheirAgeInDays()
    {
        var lines = Backlog.Lines(
        [
            new Manifest.Entry("swt-log.post.a.000001-000001.json", "1", "done", "Decrypted"),
            new Manifest.Entry("swt-log.post.a.000002-000002.json", "2", "triaged", "Decrypted"),
        ],
        name => name.EndsWith("000002-000002.json", StringComparison.Ordinal) ? Now.AddDays(-9) : null, Now);

        var line = Assert.Single(lines);
        Assert.Contains("9 days", line, StringComparison.Ordinal);
        Assert.DoesNotContain("OVERDUE", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileOlderThanFourWeeksIsMarkedOverdue()
    {
        var lines = Backlog.Lines([new Manifest.Entry("f.json", "1", "decrypted", "Decrypted")], _ => Now.AddDays(-29), Now);

        Assert.Contains("OVERDUE", Assert.Single(lines), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWeCannotDateSaysSoAndNamesWhyItIsStillHere()
    {
        var lines = Backlog.Lines([new Manifest.Entry("f.json", "1", "fetched", "WrongKey")], _ => null, Now);

        var line = Assert.Single(lines);
        Assert.Contains("age unknown", line, StringComparison.Ordinal);
        Assert.Contains("WrongKey", line, StringComparison.Ordinal);
    }
}
