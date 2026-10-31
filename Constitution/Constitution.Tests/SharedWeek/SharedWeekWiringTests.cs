using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Household;
using Constitution.Tests.MealWorker;
using static Constitution.Tests.SharedWeek.SharedWeekData;

namespace Constitution.Tests.SharedWeek;

/// <summary>The two halves of the recipes part (this phone's own kept weeks, and the weeks the other phone published) and how they are read side by side.</summary>
public class SharedWeekWiringTests
{
    private static readonly MealRecipesOutcome Nothing = new MealRecipesOutcome.Nothing(MealRecipesTrail.NoneDueSentence);
    private static readonly MealRecipesOutcome Stopped = new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.RecipesUnavailable, "Fixed words.");
    private static readonly MealRecipesOutcome Done = new MealRecipesOutcome.Done([new MealRecipesWeek(new DateOnly(2026, 10, 25), new MealDocumentsResult.Stopped(MealDocumentsStoppedBecause.NoTablet, "None."))]);

    private sealed class LocalWeeks(MealWeekRecordRead answer) : IKeptWeeks
    {
        public MealWeekRecordRead Load() => answer;
    }

    [Fact]
    public void BothQuietIsQuiet()
    {
        Assert.Same(Nothing, MealRecipesOutcomes.Combine(Nothing, Nothing));
    }

    [Fact]
    public void WhatWasSentIsNeverHiddenByAQuietHalf()
    {
        Assert.Same(Done, MealRecipesOutcomes.Combine(Done, Nothing));
        Assert.Same(Done, MealRecipesOutcomes.Combine(Nothing, Done));
    }

    [Fact]
    public void AStopIsNeverHiddenByAQuietHalf()
    {
        Assert.Same(Stopped, MealRecipesOutcomes.Combine(Stopped, Nothing));
        Assert.Same(Stopped, MealRecipesOutcomes.Combine(Nothing, Stopped));
    }

    [Fact]
    public void TwoSendsJoinTheirWeeks()
    {
        var other = new MealRecipesOutcome.Done([new MealRecipesWeek(new DateOnly(2026, 11, 1), new MealDocumentsResult.Stopped(MealDocumentsStoppedBecause.NoTablet, "None."))]);

        var joined = Assert.IsType<MealRecipesOutcome.Done>(MealRecipesOutcomes.Combine(Done, other));

        Assert.Equal([new DateOnly(2026, 10, 25), new DateOnly(2026, 11, 1)], joined.Weeks.Select(week => week.Week));
    }

    [Fact]
    public void ASendBeatsAStopInTheOtherHalf()
    {
        Assert.IsType<MealRecipesOutcome.Done>(MealRecipesOutcomes.Combine(Stopped, Done));
    }

    [Fact]
    public void ThisPhonesKeptWeeksLoseAWeekTheOtherPhonePublished()
    {
        var plans = new FakePlans { Answer = FakePlans.Held((Plan(), OwnerPhone)) };
        var kept = new KeptWeeksBesideTheHousehold(new LocalWeeks(MealWorkerData.Kept(new DateOnly(2026, 10, 25), MealWorkerData.NextSunday)), plans);

        var read = Assert.IsType<MealWeekRecordRead.Held>(kept.Load());

        Assert.Equal(MealWorkerData.NextSunday, Assert.Single(read.Record.Weeks).WeekStart);
    }

    [Fact]
    public void AFileThatWillNotOpenLeavesThisPhonesKeptWeeksAsTheyAre()
    {
        var plans = new FakePlans { Answer = new WeekPlansRead.Blocked("The household file cannot be opened.") };
        var local = MealWorkerData.Kept(new DateOnly(2026, 10, 25));

        var kept = new KeptWeeksBesideTheHousehold(new LocalWeeks(local), plans);

        Assert.Same(local, kept.Load());
    }

    [Fact]
    public void ItIsReadOnlyByItsType()
    {
        Assert.False(typeof(IMealWeekRecords).IsAssignableFrom(typeof(KeptWeeksBesideTheHousehold)));
        Assert.True(typeof(IKeptWeeks).IsAssignableFrom(typeof(IMealWeekRecords)));
    }
}
