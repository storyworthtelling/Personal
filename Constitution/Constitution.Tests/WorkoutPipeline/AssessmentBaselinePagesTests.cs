using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using PlannedTest = Constitution.Domain.Workouts.AssessmentTest;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The baselines are read from what the assessment pages actually print. A number box is painted as <c>result</c> and a
/// circled word as <c>choice</c> on every test page, so the measure a box answers is told by its page, not by an id: these
/// tests build the real pages, put ink on them, resolve it with the real resolver and read the baselines off the result.
/// </summary>
public class AssessmentBaselinePagesTests
{
    private static readonly DateTimeOffset Slot = Est(11, 3, 18);
    private static readonly DateOnly TheDay = new(2026, 11, 3);

    private static AssessmentSession Of(PlannedTest test) =>
        AssessmentPlanner.Plan(Request(Calendars(), bothAdults: false)).Sessions.First(item => item.Test == test);

    private static ExpectedDocument ExpectedFor(AssessmentSession session)
    {
        var model = AssessmentDocumentBuilder.Build(session, Slot, Zone, 1, 3, []);
        return AssessmentDocumentBuilder.Expected(model, session, "Sam", TheDay, null);
    }

    /// <summary>The pages as the read-back resolves them: every asked box read confidently, with the named ink on it.</summary>
    private static IReadOnlyList<PageResolutions> Resolved(ExpectedDocument document, Func<int, InkTarget, InkTargetRead> mark) =>
        document.Pages
            .Select(page =>
            {
                var read = InkPageRead.Read(page.AskedTargets.Select(target => mark(page.Number, target)));
                return new PageResolutions(page.Number, new InkResolver(page.Readout).Resolve(read, page.Rules, Slot.AddDays(1)));
            })
            .ToList();

    private static InkTargetRead Empty(InkTarget target) => new(target.Id, false, 0.95);

    [Fact]
    public void StrengthNumbersComeFromTheirOwnPages()
    {
        var session = Of(PlannedTest.StrengthBaselines);
        var pages = Resolved(ExpectedFor(session), (page, target) =>
            target.Id == WorkoutIds.Result ? new InkTargetRead(target.Id, true, 0.95, page == 2 ? "14" : "9") : Empty(target));

        var read = AssessmentBaselines.FromPages(session.Measures, pages);

        Assert.Equal(14m, read.Numbers[AssessmentCatalogue.SitToStandId]);
        Assert.Equal(9m, read.Numbers[AssessmentCatalogue.WallPushUpsId]);
        Assert.Empty(read.Missing);
    }

    [Fact]
    public void ABlankBoxIsMissingAndTheOtherReads()
    {
        var session = Of(PlannedTest.StrengthBaselines);
        var pages = Resolved(ExpectedFor(session), (page, target) =>
            target.Id == WorkoutIds.Result && page == 3 ? new InkTargetRead(target.Id, true, 0.95, "11") : Empty(target));

        var read = AssessmentBaselines.FromPages(session.Measures, pages);

        Assert.Equal(11m, read.Numbers[AssessmentCatalogue.WallPushUpsId]);
        Assert.DoesNotContain(AssessmentCatalogue.SitToStandId, read.Numbers.Keys);
        Assert.Equal([AssessmentCatalogue.SitToStandId], read.Missing);
    }

    [Fact]
    public void TheWalkDistanceIsRead()
    {
        var session = Of(PlannedTest.TimedWalk);
        var pages = Resolved(ExpectedFor(session), (_, target) =>
            target.Id == WorkoutIds.Result ? new InkTargetRead(target.Id, true, 0.95, "0.75") : Empty(target));

        var read = AssessmentBaselines.FromPages(session.Measures, pages);

        Assert.Equal(0.75m, read.Numbers[AssessmentCatalogue.TimedWalkId]);
    }

    [Fact]
    public void TheCircledWordIsReadOffTheMobilityPage()
    {
        var session = Of(PlannedTest.MobilityCheck);
        var pages = Resolved(ExpectedFor(session), (_, target) =>
            new InkTargetRead(target.Id, target.Kind == InkTargetKind.CircleLane && target.Meaning == "Ankles", 0.95));

        var read = AssessmentBaselines.FromPages(session.Measures, pages);

        Assert.Equal("Ankles", read.Choices[AssessmentCatalogue.ToeReachId]);
        Assert.Empty(read.Missing);
    }

    [Fact]
    public void NothingCircledLeavesTheMeasureMissing()
    {
        var session = Of(PlannedTest.MobilityCheck);
        var pages = Resolved(ExpectedFor(session), (_, target) => Empty(target));

        var read = AssessmentBaselines.FromPages(session.Measures, pages);

        Assert.Equal([AssessmentCatalogue.ToeReachId], read.Missing);
    }

    [Fact]
    public void APageTheReadDidNotGetToIsMissingNotZero()
    {
        var session = Of(PlannedTest.StrengthBaselines);
        var document = ExpectedFor(session);
        var onlyTheFirst = Resolved(document, (_, target) =>
            target.Id == WorkoutIds.Result ? new InkTargetRead(target.Id, true, 0.95, "12") : Empty(target)).Take(1);

        var read = AssessmentBaselines.FromPages(session.Measures, onlyTheFirst);

        Assert.Equal(12m, read.Numbers[AssessmentCatalogue.SitToStandId]);
        Assert.Equal([AssessmentCatalogue.WallPushUpsId], read.Missing);
    }

    [Fact]
    public void EveryTestIsOnThePageTheBuilderPutItOn()
    {
        var session = Of(PlannedTest.StrengthBaselines);
        var document = ExpectedFor(session);

        for (var index = 0; index < session.Measures.Count; index++)
        {
            Assert.Contains(document.Pages, page => page.Number == AssessmentDocument.PageOfTest(index));
        }
    }
}
