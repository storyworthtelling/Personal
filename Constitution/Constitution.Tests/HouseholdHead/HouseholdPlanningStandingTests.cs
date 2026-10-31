using Constitution.Domain.Household.Lease;
using Constitution.Infrastructure.Household;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdHead;

public class HouseholdPlanningStandingTests
{
    [Fact]
    public async Task AnUnknownRoleMayNotPlan()
    {
        using var rig = new HeadRig(role: HouseholdFileRole.Unknown);

        var standing = await new HouseholdPlanningStanding(rig.Service).AskAsync(CancellationToken.None);

        Assert.False(standing.MayPlan);
        Assert.Null(standing.Decision);
        Assert.Contains("whose phone", standing.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableFileMayNotPlan()
    {
        using var rig = new HeadRig();
        rig.Drive.Content = "this is not an envelope";
        await rig.Service.SyncAsync(CancellationToken.None);

        var standing = await new HouseholdPlanningStanding(rig.Service).AskAsync(CancellationToken.None);

        Assert.False(standing.MayPlan);
        Assert.Equal(LeaseReason.DocumentUnreadable, standing.Decision!.Reason);
    }

    [Fact]
    public async Task TheOwnerTakesTheWeekThenMayPlanOnceSettled()
    {
        using var rig = new HeadRig();
        var standing = new HouseholdPlanningStanding(rig.Service);
        await rig.Service.SyncAsync(CancellationToken.None);

        var first = await standing.AskAsync(CancellationToken.None);
        var taken = await standing.ClaimAsync(CancellationToken.None);
        rig.Now = rig.Now.Add(LeaseTerms.SettleWindow).AddMinutes(1);
        await rig.Service.SyncAsync(CancellationToken.None);
        var later = await standing.AskAsync(CancellationToken.None);

        Assert.Equal(LeaseVerdict.Take, first.Decision!.Verdict);
        Assert.False(first.MayPlan);
        Assert.False(taken.MayPlan);
        Assert.True(later.MayPlan);
    }

    [Fact]
    public async Task AskingWritesNothing()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var before = await rig.Local.LoadAsync(CancellationToken.None);

        await new HouseholdPlanningStanding(rig.Service).AskAsync(CancellationToken.None);

        Assert.True((await rig.Local.LoadAsync(CancellationToken.None)).ContentEquals(before));
    }

    [Fact]
    public async Task ThePartnerHasNoClaimToTakeWithoutAnOwner()
    {
        using var rig = new HeadRig(role: HouseholdFileRole.SharedWith);
        await rig.Local.SaveAsync(HouseholdDocument.Empty, CancellationToken.None);

        var standing = await new HouseholdPlanningStanding(rig.Service).AskAsync(CancellationToken.None);

        Assert.False(standing.MayPlan);
        Assert.Equal(LeaseReason.OwnerNeverSeen, standing.Decision!.Reason);
    }
}
