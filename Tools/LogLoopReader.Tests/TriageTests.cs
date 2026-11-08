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
    public void TheLedgerIsUpdatedFromDecisionsAndRoundTripsAsFingerprintsOnly()
    {
        var items = Triage.Plan([Group("a")], TriageLedger.Empty)
            .Select(i => i with { Outcome = TriageOutcome.Fix, Class = "C2" }).ToList();

        var json = Triage.Merge(TriageLedger.Empty, items, "2026-11-08").ToJson();

        Assert.Equal(TriageOutcome.Fix, TriageLedger.FromJson(json).Get("a")!.Outcome);
        Assert.DoesNotContain("sample", json, StringComparison.Ordinal);
        Assert.DoesNotContain("k-a", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyOrMissingLedgerReadsAsEmpty()
    {
        Assert.Null(TriageLedger.FromJson("").Get("a"));
        Assert.Null(TriageLedger.FromJson("{}").Get("a"));
    }
}
