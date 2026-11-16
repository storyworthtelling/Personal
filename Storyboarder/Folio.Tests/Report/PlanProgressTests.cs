using Folio.Domain.Analysis;
using Folio.Domain.Imaging;
using Folio.Domain.Markup;
using Folio.Domain.Model;
using Folio.Domain.Readiness;
using Folio.Domain.Report;

namespace Folio.Tests.Report;

/// <summary>
/// On a later draft each plan item says how it stands against the last one (R19): new, still open, or
/// regressed. Where the item rests on findings Folio tracks from draft to draft, their threads decide;
/// otherwise the plan pass's own comparison with the last plan stands.
/// </summary>
public class PlanProgressTests
{
    private static FindingEntry Entry(string key, string detail) =>
        new(key, "Plot holes", "Plot hole", "Chapter 2", detail, ReadinessTier.Should)
        {
            Category = FindingCategory.PlotHole, Said = detail,
        };

    private static ReportFindingView Thread(string detail, FindingStatus status) =>
        new(FindingCategory.PlotHole, "Chapter 2", detail, status);

    private static readonly IReadOnlyList<FindingEntry> Catalogue =
        [Entry("fa", "Nobody reacts."), Entry("fb", "The padlock is never explained."), Entry("fc", "The boat sinks twice.")];

    [Fact]
    public void ARegressedThreadMarksTheItemRegressed()
    {
        var item = new RevisionItem("Must", "Ch. 2", "i", "s") { Draws = ["fa", "fb"] };
        IReadOnlyList<ReportFindingView> threads =
            [Thread("Nobody reacts.", FindingStatus.Carried), Thread("The padlock is never explained.", FindingStatus.Regressed)];

        Assert.Equal(PlanSince.Regressed, PlanProgress.Since(item, Catalogue, threads, laterDraft: true));
    }

    [Fact]
    public void ACarriedThreadMarksTheItemStillOpen()
    {
        var item = new RevisionItem("Must", "Ch. 2", "i", "s") { Draws = ["fa", "fc"] };
        IReadOnlyList<ReportFindingView> threads =
            [Thread("Nobody reacts.", FindingStatus.Carried), Thread("The boat sinks twice.", FindingStatus.New)];

        Assert.Equal(PlanSince.StillOpen, PlanProgress.Since(item, Catalogue, threads, laterDraft: true));
    }

    [Fact]
    public void OnlyNewThreadsMarkTheItemNew()
    {
        var item = new RevisionItem("Must", "Ch. 2", "i", "s") { Draws = ["fc"] };

        Assert.Equal(PlanSince.New, PlanProgress.Since(item, Catalogue, [Thread("The boat sinks twice.", FindingStatus.New)], laterDraft: true));
    }

    /// <summary>An item resting on no tracked finding takes the plan pass's word.</summary>
    [Theory]
    [InlineData("open", PlanSince.StillOpen)]
    [InlineData("Regressed", PlanSince.Regressed)]
    [InlineData("new", PlanSince.New)]
    public void WithoutAThreadThePlanPassesWordStands(string since, PlanSince expected)
    {
        var item = new RevisionItem("Must", "Ch. 2", "i", "s") { Since = since };

        Assert.Equal(expected, PlanProgress.Since(item, Catalogue, [], laterDraft: true));
    }

    [Fact]
    public void AWordTheModelInventedLeavesTheItemUnmarked()
    {
        Assert.Null(PlanProgress.Since(new RevisionItem("Must", "", "i", "s") { Since = "maybe" }, Catalogue, [], laterDraft: true));
    }

    [Fact]
    public void AFirstDraftMarksNothing()
    {
        var item = new RevisionItem("Must", "Ch. 2", "i", "s") { Draws = ["fa"], Since = "open" };

        Assert.Null(PlanProgress.Since(item, Catalogue, [Thread("Nobody reacts.", FindingStatus.Carried)], laterDraft: false));
    }

    [Theory]
    [InlineData(PlanSince.New, "New since the last draft")]
    [InlineData(PlanSince.StillOpen, "Still open from the last draft")]
    [InlineData(PlanSince.Regressed, "Regressed since the last draft")]
    public void EachMarkIsWords(PlanSince since, string words)
    {
        Assert.Equal(words, PlanProgress.Words(since));
    }

    [Fact]
    public void TheCatalogueKnowsWhichTrackedListAFindingCameFrom()
    {
        var analysis = BookAnalysisResult.Empty with { PlotHoles = [new ReportFinding("Chapter 2", "Nobody reacts.")] };

        var entry = Assert.Single(FindingCatalogue.Of(analysis));

        Assert.Equal(FindingCategory.PlotHole, entry.Category);
        Assert.Equal("Nobody reacts.", entry.Said);
    }
}
