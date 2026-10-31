using Constitution.Domain.Household;
using Constitution.Infrastructure.Household;
using Constitution.Tests.HouseholdFile;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdHead;

public class HouseholdRosterTests
{
    private static async Task Hold(HeadRig rig, HouseholdDocument document) =>
        await rig.Local.SaveAsync(document, CancellationToken.None);

    [Fact]
    public async Task TheRosterIsReadFromTheFile()
    {
        using var rig = new HeadRig();
        await Hold(rig, RosterData.Household());
        await rig.Service.SyncAsync(CancellationToken.None);

        var roster = rig.Service.Roster;

        Assert.NotNull(roster);
        Assert.Equal(4, roster.Entries.Count);
        Assert.Contains(roster.Entries, entry => entry.Name == "Sam");
    }

    [Fact]
    public void NothingReadMeansNoRoster()
    {
        using var rig = new HeadRig();

        Assert.Null(rig.Service.Roster);
    }

    [Fact]
    public async Task AnUnreadableFileMeansNoRoster()
    {
        using var rig = new HeadRig();
        await Hold(rig, RosterData.Household());
        rig.Drive.Content = "this is not an envelope";
        await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Null(rig.Service.Roster);
    }

    [Fact]
    public async Task ASyncedEmptyHouseholdIsAnEmptyRoster()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Empty(rig.Service.Roster!.Entries);
    }

    [Fact]
    public async Task APartnerRosterIsReadOnly()
    {
        using var rig = new HeadRig(role: HouseholdFileRole.SharedWith);
        await Hold(rig, RosterData.Household());
        await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(RosterRole.Partner, rig.Service.RosterRole);
        Assert.False(new RosterEditor(rig.Service.RosterRole).CanEdit);
    }

    [Fact]
    public void AnUnknownRoleHasTheOwnersRosterRights()
    {
        using var rig = new HeadRig(role: HouseholdFileRole.Unknown);

        Assert.Equal(RosterRole.Owner, rig.Service.RosterRole);
    }
}
