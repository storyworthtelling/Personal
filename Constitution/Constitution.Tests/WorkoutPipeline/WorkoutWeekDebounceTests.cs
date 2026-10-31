using Constitution.Domain.Workouts.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The app opens many times a day and the Workouts tab asks for the week each time it is shown; the run reads calendars and the tablet,
/// so an open that follows a good run closely reuses it. The scheduled run never waits on that, and a run that could not plan is never
/// reused: the owner who has just signed in or shared a calendar is opening the app to see it work.
/// </summary>
public class WorkoutWeekDebounceTests
{
    [Fact]
    public void TheScheduledRunAlwaysRuns()
    {
        var now = Est(11, 4, 6, 45);

        Assert.True(WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Scheduled, now, now));
        Assert.True(WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Scheduled, now.AddMinutes(-1), now));
    }

    [Fact]
    public void AnOpenWithNoRunBeforeRuns()
    {
        Assert.True(WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Opened, null, Est(11, 4, 8)));
    }

    [Fact]
    public void AnOpenSoonAfterAGoodRunReusesIt()
    {
        var now = Est(11, 4, 8, 0);

        Assert.False(WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Opened, now.AddMinutes(-1), now));
        Assert.False(WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Opened, now - (WorkoutWeekDebounce.OpenGap - TimeSpan.FromTicks(1)), now));
    }

    [Fact]
    public void AnOpenOnceTheGapHasPassedRuns()
    {
        var now = Est(11, 4, 8, 0);

        Assert.True(WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Opened, now - WorkoutWeekDebounce.OpenGap, now));
        Assert.True(WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Opened, now.AddHours(-23), now));
    }

    [Fact]
    public void ARunFromTheFutureIsNotTrusted()
    {
        // The clock was set back: a "last run" ahead of now says nothing about how fresh the plan is.
        var now = Est(11, 4, 8, 0);

        Assert.True(WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Opened, now.AddMinutes(5), now));
    }

    [Fact]
    public void TheGapIsAFewMinutesNotHours()
    {
        Assert.InRange(WorkoutWeekDebounce.OpenGap, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ARunThatPlannedNothingLeavesNothingToReuse()
    {
        // The head hands over the time of the last run that planned the week, and null when the last run did not: the next open runs again.
        var now = Est(11, 4, 8, 0);

        Assert.True(WorkoutWeekDebounce.ShouldRun(WorkoutWeekTrigger.Opened, null, now));
    }
}
