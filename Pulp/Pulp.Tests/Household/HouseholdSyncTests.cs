using Pulp.Domain.Actions;
using Pulp.Domain.Household;
using HouseholdSync = Pulp.Domain.Household.HouseholdSync;

namespace Pulp.Tests.Household;

/// <summary>
/// The sync policy around the shared household file (finding §2.7): the phone's own file is the
/// working copy, Drive is where the two phones meet. These are the decisions — when to pass, what to
/// keep after one, what the screens are told — with time passed in, so none of it needs Drive.
/// </summary>
public class HouseholdSyncTests
{
    private static readonly DateTimeOffset Opened = new(2026, 3, 14, 19, 0, 0, TimeSpan.Zero);

    // ---- who may reach Drive, and who may create the file ----

    [Fact]
    public void WithoutAPassphraseThePhoneStaysLocalAndSaysWhy()
    {
        var status = HouseholdSync.Readiness(hasPassphrase: false, hasAccount: true);

        Assert.NotNull(status);
        Assert.Equal(HouseholdSyncState.LocalOnly, status!.State);
        Assert.Contains("passphrase", status.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithoutAnAccountThePhoneStaysLocalAndSaysWhy()
    {
        var status = HouseholdSync.Readiness(hasPassphrase: true, hasAccount: false);

        Assert.NotNull(status);
        Assert.Equal(HouseholdSyncState.LocalOnly, status!.State);
        Assert.Contains("Google account", status.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void WithBothThereIsNothingStoppingAPass() =>
        Assert.Null(HouseholdSync.Readiness(hasPassphrase: true, hasAccount: true));

    // ---- what the screens are told ----

    [Fact]
    public void AWrongPassphraseRaisesTheUnreadableNotice()
    {
        var status = HouseholdSyncStatus.Unreadable(HouseholdUnreadableReason.WrongPassphrase, "The passphrase does not open it.", Opened);

        Assert.Equal(WaitingNotices.HouseholdUnreadable(Opened), status.Notice(Opened));
    }

    [Fact]
    public void ANewerFileRaisesTheNewerVersionNotice()
    {
        var status = HouseholdSyncStatus.Unreadable(HouseholdUnreadableReason.NewerFormat, "Format 9.", Opened);

        Assert.Equal(WaitingNotices.HouseholdFromNewerVersion(Opened), status.Notice(Opened));
    }

    [Fact]
    public void SyncedOfflineAndLocalOnlyRaiseNoNotice()
    {
        Assert.Null(HouseholdSyncStatus.Synced(Opened).Notice(Opened));
        Assert.Null(HouseholdSyncStatus.Offline(Opened, "No network.").Notice(Opened));
        Assert.Null(HouseholdSyncStatus.LocalOnly("No passphrase.").Notice(Opened));
    }

    [Fact]
    public void GoingOfflineRemembersWhenItLastSynced()
    {
        var status = HouseholdSyncStatus.Synced(Opened).WentOffline("No network.");

        Assert.Equal(HouseholdSyncState.Offline, status.State);
        Assert.Equal(Opened, status.LastSynced);
        Assert.Equal("No network.", status.Reason);
    }

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
