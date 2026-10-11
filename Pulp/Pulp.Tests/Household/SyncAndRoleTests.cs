using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

/// <summary>
/// The second phone (D166, mockup 09) and the sync line. Both adults manage the children; the owner
/// alone changes the services and adds or removes viewers, so on the phone the household is shared
/// with those are read-only. A phone whose role is not known yet is not restricted: that is the owner's
/// own phone before the vault has been read, or a phone with no vault at all, where nobody else could
/// be waiting. A screen re-reads the household when a sync pass brought records home or the status
/// itself moved, not on every quiet pass.
/// </summary>
public class SyncAndRoleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheOwnerChangesEverything()
    {
        var rights = HouseholdPermissions.For(HouseholdFileRole.Owner);

        Assert.True(rights.CanEditServices);
        Assert.True(rights.CanAddOrRemoveViewers);
        Assert.Null(rights.Note);
    }

    [Fact]
    public void TheSharedWithPhoneReadsServicesAndViewersButManagesTheChildren()
    {
        var rights = HouseholdPermissions.For(HouseholdFileRole.SharedWith);

        Assert.False(rights.CanEditServices);
        Assert.False(rights.CanAddOrRemoveViewers);
        Assert.True(rights.CanEditChildren);
        Assert.True(rights.CanRenameOwnViewer);
        Assert.Equal(HouseholdPermissions.OwnerOnlyNote, rights.Note);
    }

    [Fact]
    public void AnUnknownRoleIsNotRestricted()
    {
        var rights = HouseholdPermissions.For(HouseholdFileRole.Unknown);

        Assert.True(rights.CanEditServices);
        Assert.True(rights.CanAddOrRemoveViewers);
    }

    [Fact]
    public void ARefreshIsNeededWhenAPassBroughtRecordsHome()
    {
        var before = HouseholdSyncStatus.Synced(Now);
        var after = HouseholdSyncStatus.Synced(Now.AddMinutes(1)) with { BroughtRecords = true };

        Assert.True(SyncRefresh.Needed(before, after));
    }

    [Fact]
    public void ARefreshIsNeededWhenTheStatusMoved()
    {
        Assert.True(SyncRefresh.Needed(HouseholdSyncStatus.Synced(Now), HouseholdSyncStatus.Offline(Now, "No network.")));
    }

    [Fact]
    public void AQuietPassThatChangedNothingNeedsNoRefresh()
    {
        Assert.False(SyncRefresh.Needed(HouseholdSyncStatus.Synced(Now), HouseholdSyncStatus.Synced(Now.AddMinutes(1))));
    }

    [Fact]
    public void TheFirstStatusAScreenSeesNeedsARefresh() =>
        Assert.True(SyncRefresh.Needed(null, HouseholdSyncStatus.Synced(Now)));

    [Theory]
    [InlineData(0, "Synced with Drive just now.")]
    [InlineData(5, "Synced with Drive 5 min ago.")]
    [InlineData(180, "Synced with Drive 3 h ago.")]
    public void ASyncedLineSaysHowLongAgo(int minutesAgo, string expected) =>
        Assert.Equal(expected, HouseholdSyncText.Line(HouseholdSyncStatus.Synced(Now.AddMinutes(-minutesAgo)), Now));

    [Fact]
    public void AnOldSyncNamesTheDay() =>
        Assert.Equal("Synced with Drive on Oct 2.", HouseholdSyncText.Line(HouseholdSyncStatus.Synced(Now.AddDays(-10)), Now));

    [Fact]
    public void OfflineSaysTheChangesAreKept()
    {
        var line = HouseholdSyncText.Line(HouseholdSyncStatus.Offline(Now.AddMinutes(-5), "timeout"), Now);

        Assert.Equal("Drive couldn't be reached. Changes are kept on this phone and go up when it can.", line);
    }

    [Fact]
    public void WaitingForTheShareSaysItWaits()
    {
        var line = HouseholdSyncText.Line(HouseholdSyncStatus.WaitingForShare(null, "ignored detail"), Now);

        Assert.Equal("Waiting for the owner to share the household with this account.", line);
    }

    [Fact]
    public void LocalOnlyAndUnreadableCarryTheirOwnSentence()
    {
        Assert.Equal("Enter the vault passphrase.", HouseholdSyncText.Line(HouseholdSyncStatus.LocalOnly("Enter the vault passphrase."), Now));
        Assert.Equal("Damaged.", HouseholdSyncText.Line(HouseholdSyncStatus.Unreadable(HouseholdUnreadableReason.Damaged, "Damaged.", null), Now));
    }
}
