using Constitution.Domain.Meals.Pipeline;
using Constitution.Tests.Documents;
using Constitution.Tests.MealDeadlineStep;
using Constitution.Tests.MealWeekRun;
using static Constitution.Tests.Calendar.InventedFamily;
using Run = Constitution.Infrastructure.Meals.MealWeekRun;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>D112: the decision record's lines reach the lists built from it, and an unreadable record never blocks the sheet or goes unsaid.</summary>
public class MealExtraLinesTests
{
    private static MealDecisionEntry Entry(DateOnly week, params string[] evenings) =>
        new(week, Edt(10, 25, 15), 5, evenings.Length, 0, false) { DeniedEvenings = evenings };

    private static MemoryDecisions Holding(DateOnly week, params string[] evenings) =>
        new() { Json = MealDecisionRecordCodec.Encode(MealDecisionRecord.Empty.With(Entry(week, evenings))) };

    private static (MealWeekRig Rig, DocsRig Docs) Rigs(IMealDecisionRecords? decisions)
    {
        var rig = new MealWeekRig();
        var docs = new DocsRig();
        rig.Box.Of(DocsData.Cards().Concat(MealPlanning.MealData.Box()).ToList());
        rig.Decisions = decisions;
        return (rig, docs);
    }

    private static Task<MealWeekOutcome> RunAsync(MealWeekRig rig, DocsRig docs) =>
        Run.RunAsync(rig.Inputs() with { DeliverFor = [Lee] }, rig.Seams with { Documents = docs.Seams, Decisions = rig.Decisions }, CancellationToken.None);

    private static string SheetText(DocsRig docs) =>
        string.Join("\n", PdfReadBack.Open(docs.Tablet.PdfOf(docs.SheetTitle)).GetPages().Select(page => page.Text));

    [Fact]
    public void NoRecordGivesNoLines()
    {
        var read = MealExtraLines.Read(null, Sunday);

        Assert.Empty(read.Lines);
        Assert.Null(read.UnreadableBecause);
    }

    [Fact]
    public void NothingYetGivesNoLines()
    {
        var read = MealExtraLines.Read(new MemoryDecisions(), Sunday);

        Assert.Empty(read.Lines);
        Assert.Null(read.UnreadableBecause);
    }

    [Fact]
    public void ADecidedWeekGivesItsLines()
    {
        var read = MealExtraLines.Read(Holding(Sunday, "tuesday", "friday"), Sunday);

        Assert.Equal(["Tuesday dinner", "Friday dinner"], read.Lines);
        Assert.Null(read.UnreadableBecause);
    }

    [Fact]
    public void AnotherWeeksDenialsAreNotThisWeeks()
    {
        var read = MealExtraLines.Read(Holding(Sunday.AddDays(-7), "tuesday"), Sunday);

        Assert.Empty(read.Lines);
    }

    [Fact]
    public void AnUnreadableRecordGivesTheReasonAndNoLines()
    {
        var read = MealExtraLines.Read(new MemoryDecisions { Json = "{ not json" }, Sunday);

        Assert.Empty(read.Lines);
        Assert.False(string.IsNullOrWhiteSpace(read.UnreadableBecause));
    }

    [Fact]
    public async Task TheSheetCarriesTheDeniedEveningsLine()
    {
        var (rig, docs) = Rigs(Holding(Sunday, "tuesday"));

        await RunAsync(rig, docs);

        Assert.Contains("Tuesday dinner", SheetText(docs), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARunWithoutTheRecordPrintsNoSuchLine()
    {
        var (rig, docs) = Rigs(null);

        await RunAsync(rig, docs);

        Assert.DoesNotContain("Tuesday dinner", SheetText(docs), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableRecordStillDeliversTheSheetAndSaysSo()
    {
        var (rig, docs) = Rigs(new MemoryDecisions { Json = "{ not json" });

        var report = Assert.IsType<MealWeekOutcome.Planned>(await RunAsync(rig, docs)).Report;

        Assert.Equal(MealDocumentVerdict.Delivered, docs.Sheet(Assert.IsType<MealDocumentsResult.Done>(report.Documents)).Verdict);
        Assert.False(string.IsNullOrWhiteSpace(report.ExtraLinesUnreadable));
        Assert.Contains(", decisions unreadable", rig.Log.Phrases.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecordThatReadsFineAddsNothingToTheTrail()
    {
        var (rig, docs) = Rigs(Holding(Sunday, "tuesday"));

        var report = Assert.IsType<MealWeekOutcome.Planned>(await RunAsync(rig, docs)).Report;

        Assert.Null(report.ExtraLinesUnreadable);
        Assert.DoesNotContain("decisions", rig.Log.Phrases.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADenialAfterTheDeadlineNeverReplacesThePrintedSheet()
    {
        var (rig, docs) = Rigs(null);
        await RunAsync(rig, docs);
        var uploads = docs.Tablet.Uploads;
        rig.Now = Edt(10, 25, 15);
        docs.Now = Edt(10, 25, 15);

        var before = Assert.IsType<MealWeekOutcome.Planned>(await RunAsync(rig, docs)).Report;
        rig.Decisions = Holding(Sunday, "tuesday");
        var after = Assert.IsType<MealWeekOutcome.Planned>(await RunAsync(rig, docs)).Report;

        Assert.Equal(MealDocumentVerdict.Unchanged, docs.Sheet(Assert.IsType<MealDocumentsResult.Done>(before.Documents)).Verdict);
        Assert.Equal(MealDocumentVerdict.TooLate, docs.Sheet(Assert.IsType<MealDocumentsResult.Done>(after.Documents)).Verdict);
        Assert.Equal(uploads, docs.Tablet.Uploads);
    }
}
