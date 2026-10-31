using Storyworthtelling.Household;

namespace Storyworthtelling.Household.Tests;

/// <summary>
/// When the next sync pass is due, with time passed in. Copied from Pulp's HouseholdSyncTests.
/// </summary>
public class HouseholdSyncScheduleTests
{
    private static readonly DateTimeOffset Opened = new(2026, 3, 14, 19, 0, 0, TimeSpan.Zero);

    // ---- when to pass ----

    [Fact]
    public void OpeningThePhoneIsDueAtOnce()
    {
        var schedule = new HouseholdSyncSchedule();
        schedule.Opened(Opened);

        Assert.True(schedule.IsDue(Opened));
    }

    [Fact]
    public void AnEditWaitsForTheDebounce()
    {
        var schedule = new HouseholdSyncSchedule();
        schedule.Edited(Opened);

        Assert.False(schedule.IsDue(Opened + HouseholdSyncSchedule.Debounce - TimeSpan.FromMilliseconds(1)));
        Assert.True(schedule.IsDue(Opened + HouseholdSyncSchedule.Debounce));
    }

    [Fact]
    public void AnotherEditPushesThePassBack()
    {
        var schedule = new HouseholdSyncSchedule();
        schedule.Edited(Opened);
        var second = Opened + TimeSpan.FromSeconds(3);
        schedule.Edited(second);

        Assert.False(schedule.IsDue(Opened + HouseholdSyncSchedule.Debounce));
        Assert.True(schedule.IsDue(second + HouseholdSyncSchedule.Debounce));
    }

    [Fact]
    public void NothingIsDueAfterAPassThatLanded()
    {
        var schedule = new HouseholdSyncSchedule();
        schedule.Opened(Opened);
        schedule.Started(Opened);
        schedule.Succeeded();

        Assert.Null(schedule.NextDue);
        Assert.False(schedule.IsDue(Opened + TimeSpan.FromDays(1)));
    }

    [Fact]
    public void AnEditDuringAPassIsCarriedByAnotherOne()
    {
        var schedule = new HouseholdSyncSchedule();
        schedule.Opened(Opened);
        schedule.Started(Opened);
        var edit = Opened + TimeSpan.FromSeconds(1);
        schedule.Edited(edit);
        schedule.Succeeded();

        Assert.True(schedule.IsDue(edit + HouseholdSyncSchedule.Debounce));
    }

    [Fact]
    public void AnUnreachableDriveIsRetriedWithBackoff()
    {
        var schedule = new HouseholdSyncSchedule();
        schedule.Started(Opened);
        schedule.Failed(Opened);
        Assert.Equal(Opened + HouseholdSyncSchedule.FirstRetry, schedule.NextDue);

        var second = Opened + HouseholdSyncSchedule.FirstRetry;
        schedule.Started(second);
        schedule.Failed(second);
        Assert.Equal(second + (HouseholdSyncSchedule.FirstRetry * 2), schedule.NextDue);

        var at = second;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            schedule.Started(at);
            schedule.Failed(at);
        }

        Assert.Equal(at + HouseholdSyncSchedule.LongestRetry, schedule.NextDue);
    }

    [Fact]
    public void AnEditDoesNotHammerADriveThatIsDown()
    {
        var schedule = new HouseholdSyncSchedule();
        schedule.Started(Opened);
        schedule.Failed(Opened);
        schedule.Edited(Opened + TimeSpan.FromSeconds(1));

        Assert.Equal(Opened + HouseholdSyncSchedule.FirstRetry, schedule.NextDue);
    }

    [Fact]
    public void ALandedPassClearsTheBackoff()
    {
        var schedule = new HouseholdSyncSchedule();
        schedule.Started(Opened);
        schedule.Failed(Opened);
        schedule.Started(Opened + HouseholdSyncSchedule.FirstRetry);
        schedule.Succeeded();
        var edit = Opened + TimeSpan.FromHours(1);
        schedule.Edited(edit);

        Assert.Equal(edit + HouseholdSyncSchedule.Debounce, schedule.NextDue);
    }

    [Fact]
    public void AnUnreadableFileStopsPassesUntilSomeoneAsks()
    {
        var schedule = new HouseholdSyncSchedule();
        schedule.Started(Opened);
        schedule.Blocked();
        schedule.Edited(Opened + TimeSpan.FromSeconds(1));

        Assert.Null(schedule.NextDue);

        schedule.Asked(Opened + TimeSpan.FromMinutes(2));
        Assert.True(schedule.IsDue(Opened + TimeSpan.FromMinutes(2)));
    }
}
