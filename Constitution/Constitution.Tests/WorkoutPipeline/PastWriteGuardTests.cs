using Constitution.Domain.Placement;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.Placement.PlacementFixture;
using static Constitution.Tests.Workouts.WorkoutFixture;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// A run that comes late in a week (a phone that was off on Monday) would otherwise write events in the past for the
/// sessions that have gone by, because the week's plan must be made from its start for the keys to stay put. The guard
/// sits on the writer and refuses them, leaving the calendar and the record alone.
/// </summary>
public class PastWriteGuardTests
{
    private static EventToCreate Event(int day, int hour) =>
        new(StrengthKey(Sam, day), Sam, "Workout · 40 min", At(day, hour), At(day, hour + 1));

    [Fact]
    public async Task AnEventStillToComeIsWritten()
    {
        var calendar = new FakeCalendar();
        var guard = new PastWriteGuard(calendar, () => At(1, 9));

        var made = await guard.CreateAsync(Event(1, 18), CancellationToken.None);

        Assert.True(made.Succeeded);
        Assert.Equal(1, calendar.CreateCalls);
        Assert.Single(calendar.All);
    }

    [Fact]
    public async Task ABegunEventIsRefusedUntouched()
    {
        var calendar = new FakeCalendar();
        var guard = new PastWriteGuard(calendar, () => At(1, 9));

        var made = await guard.CreateAsync(Event(0, 18), CancellationToken.None);

        Assert.False(made.Succeeded);
        Assert.Equal(PastWriteGuard.Reason, made.FailedBecause);
        Assert.Equal(0, calendar.CreateCalls);
        Assert.Empty(calendar.All);
    }

    [Fact]
    public async Task AnEventStartingThisVeryMinuteIsStillWritten()
    {
        var calendar = new FakeCalendar();
        var guard = new PastWriteGuard(calendar, () => At(1, 18));

        Assert.True((await guard.CreateAsync(Event(1, 18), CancellationToken.None)).Succeeded);
        Assert.False((await new PastWriteGuard(new FakeCalendar(), () => At(1, 18).AddMinutes(1)).CreateAsync(Event(1, 18), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task LookingEventsUpIsPassedStraightThrough()
    {
        var calendar = new FakeCalendar();
        await calendar.CreateAsync(Event(2, 18), CancellationToken.None);
        var guard = new PastWriteGuard(calendar, () => At(1, 9));

        var found = await guard.FindAsync(Sam, [StrengthKey(Sam, 2)], CancellationToken.None);

        Assert.True(found.IsAvailable);
        Assert.Single(found.Held);
    }

    [Fact]
    public void TheReasonHoldsNothingOfTheCalendar()
    {
        Assert.False(string.IsNullOrWhiteSpace(PastWriteGuard.Reason));
        Assert.DoesNotContain(Sam, PastWriteGuard.Reason, StringComparison.Ordinal);
    }
}
