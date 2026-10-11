using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

/// <summary>A sync that cannot open the household file must say so on the dog-ear, and stop saying so when the cause goes (S4).</summary>
public class HouseholdSyncNoticesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(HouseholdUnreadableReason.WrongPassphrase)]
    [InlineData(HouseholdUnreadableReason.Damaged)]
    [InlineData(HouseholdUnreadableReason.NewerFormat)]
    [InlineData(HouseholdUnreadableReason.NoPassphrase)]
    public void AnUnreadableFileRaisesANoticeUnderOneKey(HouseholdUnreadableReason why)
    {
        var change = HouseholdSyncNotices.For(HouseholdSyncStatus.Unreadable(why, "Cannot open.", null), Now);

        Assert.NotNull(change.Raise);
        Assert.Equal(change.Raise!.Key, change.Key);
    }

    [Fact]
    public void ASyncedOrOfflineStatusWithdrawsTheSameKey()
    {
        var raised = HouseholdSyncNotices.For(HouseholdSyncStatus.Unreadable(HouseholdUnreadableReason.Damaged, "Cannot open.", null), Now);

        foreach (var status in new[] { HouseholdSyncStatus.Synced(Now), HouseholdSyncStatus.Offline(null, "timeout"), HouseholdSyncStatus.WaitingForShare(null, "x") })
        {
            var change = HouseholdSyncNotices.For(status, Now);
            Assert.Null(change.Raise);
            Assert.Equal(raised.Key, change.Key);
        }
    }

    [Fact]
    public void ANewerFormatAndADamagedFileAreDifferentSentencesOnTheSameKey()
    {
        var newer = HouseholdSyncNotices.For(HouseholdSyncStatus.Unreadable(HouseholdUnreadableReason.NewerFormat, "x", null), Now);
        var damaged = HouseholdSyncNotices.For(HouseholdSyncStatus.Unreadable(HouseholdUnreadableReason.Damaged, "x", null), Now);

        Assert.NotEqual(newer.Raise!.Headline, damaged.Raise!.Headline);
        Assert.Equal(newer.Key, damaged.Key);
    }
}
