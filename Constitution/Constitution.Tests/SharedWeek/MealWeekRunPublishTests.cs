using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Tests.MealDeadlineStep;
using Constitution.Tests.MealWeekRun;
using static Constitution.Tests.Calendar.InventedFamily;
using Run = Constitution.Infrastructure.Meals.MealWeekRun;

namespace Constitution.Tests.SharedWeek;

/// <summary>After a run keeps a week, the phone that holds the lease puts it in the household file; a failure there is said and never fails the run.</summary>
public class MealWeekRunPublishTests
{
    private sealed class FakePublisher(MealWeekRig rig) : IWeekPlanPublisher
    {
        public List<SharedWeekPlan> Plans { get; } = [];

        public List<int> SavesAtTheTime { get; } = [];

        public WeekPlanPublication Answer { get; set; } = new WeekPlanPublication.Published();

        public Task<WeekPlanPublication> PublishAsync(SharedWeekPlan plan, CancellationToken cancellation)
        {
            Plans.Add(plan);
            SavesAtTheTime.Add(rig.Records.Saves);
            rig.Calls.Add("publish");
            return Task.FromResult(Answer);
        }
    }

    private static (MealWeekRig Rig, FakePublisher Publisher) Rigs()
    {
        var rig = new MealWeekRig();
        return (rig, new FakePublisher(rig));
    }

    private static Task<MealWeekOutcome> RunAsync(MealWeekRig rig, FakePublisher publisher, IMealDecisionRecords? decisions = null) =>
        Run.RunAsync(rig.Inputs(), rig.Seams with { Publisher = publisher, Decisions = decisions }, CancellationToken.None);

    [Fact]
    public async Task TheKeptWeekIsPublished()
    {
        var (rig, publisher) = Rigs();

        var outcome = await RunAsync(rig, publisher);

        var report = Assert.IsType<MealWeekOutcome.Planned>(outcome).Report;
        var plan = Assert.Single(publisher.Plans);
        Assert.Equal(MealWeekRecordCodec.ContentKey(rig.Records.Held().For(Sunday)!), MealWeekRecordCodec.ContentKey(plan.Stored));
        Assert.IsType<WeekPlanPublication.Published>(report.Publication);
    }

    [Fact]
    public async Task ItIsPublishedAfterTheWeekIsSaved()
    {
        var (rig, publisher) = Rigs();

        await RunAsync(rig, publisher);

        Assert.Equal([1], publisher.SavesAtTheTime);
    }

    [Fact]
    public async Task TheDeadlineIsTheSundaysEightOClock()
    {
        var (rig, publisher) = Rigs();

        await RunAsync(rig, publisher);

        Assert.Equal(Edt(10, 25, 8), Assert.Single(publisher.Plans).Deadline);
    }

    [Fact]
    public async Task TheDeniedEveningsLinesTravelWithIt()
    {
        var (rig, publisher) = Rigs();
        var entry = new MealDecisionEntry(Sunday, Edt(10, 25, 15), 5, 1, 0, false) { DeniedEvenings = ["tuesday"] };
        var decisions = new MemoryDecisions { Json = MealDecisionRecordCodec.Encode(MealDecisionRecord.Empty.With(entry)) };

        await RunAsync(rig, publisher, decisions);

        Assert.Equal(["Tuesday dinner"], Assert.Single(publisher.Plans).ExtraLines);
    }

    [Fact]
    public async Task NoDecisionRecordMeansNoLines()
    {
        var (rig, publisher) = Rigs();

        await RunAsync(rig, publisher);

        Assert.Empty(Assert.Single(publisher.Plans).ExtraLines);
    }

    [Fact]
    public async Task AFailedPublishIsReportedAndTheRunStillPlanned()
    {
        var (rig, publisher) = Rigs();
        publisher.Answer = new WeekPlanPublication.NotPublished(WeekPlanNotPublishedBecause.FileUnreadable, "The household file cannot be opened.");

        var outcome = await RunAsync(rig, publisher);

        var report = Assert.IsType<MealWeekOutcome.Planned>(outcome).Report;
        Assert.IsType<WeekPlanPublication.NotPublished>(report.Publication);
        Assert.Equal(1, rig.Records.Saves);
        Assert.Contains(", week not shared", MealWeekTrail.LogPhrase(outcome), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedPublishIsNotSettledSoTheNextPassTriesAgain()
    {
        var (rig, publisher) = Rigs();
        publisher.Answer = new WeekPlanPublication.NotPublished(WeekPlanNotPublishedBecause.CopyDamaged, "The copy is damaged.");

        var outcome = await RunAsync(rig, publisher);

        Assert.False(MealPassPlan.Settles(outcome));
    }

    [Fact]
    public async Task APublishedOrUnchangedWeekSettlesAsBefore()
    {
        var (rig, publisher) = Rigs();

        Assert.True(MealPassPlan.Settles(await RunAsync(rig, publisher)));
        publisher.Answer = new WeekPlanPublication.Unchanged();
        Assert.True(MealPassPlan.Settles(await RunAsync(rig, publisher)));
    }

    [Fact]
    public async Task ARunWithNoPublisherIsAsItWas()
    {
        var rig = new MealWeekRig();

        var outcome = await rig.RunAsync();

        var report = Assert.IsType<MealWeekOutcome.Planned>(outcome).Report;
        Assert.IsType<WeekPlanPublication.NotAttempted>(report.Publication);
        Assert.DoesNotContain("shared", MealWeekTrail.LogPhrase(outcome), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePartnersPhoneNeverPublishes()
    {
        var (rig, publisher) = Rigs();
        rig.MayPlan = false;

        var outcome = await RunAsync(rig, publisher);

        Assert.Equal(MealNotPlannedBecause.NotThePlanner, Assert.IsType<MealWeekOutcome.NotPlanned>(outcome).Because);
        Assert.Empty(publisher.Plans);
    }

    [Fact]
    public async Task AStopNeverPublishes()
    {
        var (rig, publisher) = Rigs();
        rig.Box.Answer = new Constitution.Domain.Recipes.Screens.RecipeBoxRead.Unavailable("The box cannot be read.");

        await RunAsync(rig, publisher);

        Assert.Empty(publisher.Plans);
    }

    [Fact]
    public async Task APlanTheBoxCouldNotMakeIsNeverPublished()
    {
        var (rig, publisher) = Rigs();
        rig.Box.Of(rig.Box.Answer is Constitution.Domain.Recipes.Screens.RecipeBoxRead.Ready ready ? ready.Snapshot.Cards.Take(3).ToList() : []);

        var outcome = await RunAsync(rig, publisher);

        Assert.Equal(MealNotPlannedBecause.BoxTooSmall, Assert.IsType<MealWeekOutcome.NotPlanned>(outcome).Because);
        Assert.Empty(publisher.Plans);
    }
}
