using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Constitution.Domain.Household.Records;
using Constitution.Infrastructure.Household;
using Constitution.Tests.HouseholdFile;
using Constitution.Tests.Recipes;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdHead;

public class HouseholdFileServiceTests
{
    private static async Task Seed(HeadRig rig)
    {
        await rig.Service.EditAsync((document, stamp, writer) => HouseholdRecords.Put(document, RecipeMaps.Recipe, RecipeData.Chili(), stamp, writer), CancellationToken.None);
    }

    [Fact]
    public async Task TheOwnerCreatesTheFile()
    {
        using var rig = new HeadRig();
        await Seed(rig);

        var standing = await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(HouseholdFileState.Synced, standing.State);
        Assert.NotNull(rig.Drive.Content);
        Assert.Single(HouseholdRecords.Read(rig.Opened(), RecipeMaps.Recipe).Items);
    }

    [Theory]
    [InlineData(HouseholdFileRole.SharedWith)]
    [InlineData(HouseholdFileRole.Unknown)]
    public async Task ANonOwnerNeverCreatesTheFile(HouseholdFileRole role)
    {
        using var rig = new HeadRig(role: role);
        await Seed(rig);

        var standing = await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(HouseholdFileState.WaitingForShare, standing.State);
        Assert.Null(rig.Drive.Content);
        Assert.Equal(0, rig.Drive.Writes);
    }

    [Fact]
    public async Task ThePartnerAdoptsTheOwnersFile()
    {
        var owner = new HeadRig();
        await Seed(owner);
        await owner.Service.SyncAsync(CancellationToken.None);
        using var partner = new HeadRig(owner.Drive, HouseholdFileRole.SharedWith);

        var standing = await partner.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(HouseholdFileState.Synced, standing.State);
        Assert.Single(HouseholdRecords.Read(await partner.Service.ReadAsync(CancellationToken.None), RecipeMaps.Recipe).Items);
        owner.Dispose();
    }

    [Fact]
    public async Task AWrongPassphraseWritesNothing()
    {
        using var rig = new HeadRig();
        var theirs = new HeadRig();
        var other = RecipeData.Document([RecipeData.Chili()]);
        rig.Drive.Content = theirs.SealedFile(other, "some-other-passphrase");
        var before = rig.Drive.Content;
        await Seed(rig);

        var standing = await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(HouseholdFileState.Unreadable, standing.State);
        Assert.Equal(HouseholdUnreadableReason.WrongPassphrase, standing.Reason);
        Assert.Equal(before, rig.Drive.Content);
        Assert.Equal(0, rig.Drive.Writes);
        theirs.Dispose();
    }

    [Fact]
    public async Task AnUnreadableFileRefusesEveryEdit()
    {
        using var rig = new HeadRig();
        rig.Drive.Content = "this is not an envelope";
        await rig.Service.SyncAsync(CancellationToken.None);

        await Assert.ThrowsAsync<HouseholdUnreadableException>(() => Seed(rig));

        Assert.Empty((await rig.Local.LoadAsync(CancellationToken.None)).Records);
        Assert.Equal(0, rig.Drive.Writes);
    }

    [Fact]
    public async Task AskingAgainLiftsTheBlockOnceItIsReadable()
    {
        using var rig = new HeadRig();
        rig.Drive.Content = "this is not an envelope";
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.Drive.Content = null;

        var standing = await rig.Service.SyncAsync(CancellationToken.None);

        Assert.NotEqual(HouseholdFileState.Unreadable, standing.State);
    }

    [Fact]
    public async Task NoPassphraseTouchesNoDrive()
    {
        using var rig = new HeadRig { HeldPassphrase = string.Empty };
        await Seed(rig);

        var standing = await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(HouseholdFileState.LocalOnly, standing.State);
        Assert.Contains("passphrase", standing.Sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, rig.Drive.Reads);
    }

    [Fact]
    public async Task NoAccountTouchesNoDrive()
    {
        using var rig = new HeadRig { HasAccount = false };

        var standing = await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(HouseholdFileState.LocalOnly, standing.State);
        Assert.Contains("Google", standing.Sentence, StringComparison.Ordinal);
        Assert.Equal(0, rig.Drive.Reads);
    }

    [Fact]
    public async Task AnOfflineDriveKeepsTheEditOnThePhone()
    {
        using var rig = new HeadRig();
        rig.Drive.Failure = new IOException("no route");
        await Seed(rig);

        var standing = await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(HouseholdFileState.Offline, standing.State);
        Assert.Single(HouseholdRecords.Read(await rig.Local.LoadAsync(CancellationToken.None), RecipeMaps.Recipe).Items);
    }

    [Fact]
    public async Task AnEditAsksForAPass()
    {
        using var rig = new HeadRig();
        var asked = 0;
        rig.Service.SyncWanted += () => asked++;

        await Seed(rig);

        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task ADamagedLocalCopyIsNeverWrittenOver()
    {
        using var rig = new HeadRig();
        await Seed(rig);
        var path = Directory.GetFiles(Path.GetDirectoryName(rig.Install.FilePath)!, "household.json").Single();
        await File.WriteAllTextAsync(path, "{ damaged");

        var standing = await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(HouseholdFileState.Unreadable, standing.State);
        Assert.Equal(HouseholdUnreadableReason.Damaged, standing.Reason);
        Assert.Equal("{ damaged", await File.ReadAllTextAsync(path));
        Assert.Equal(0, rig.Drive.Writes);
    }

    [Fact]
    public async Task AnOwnerPassWritesItsHeartbeat()
    {
        using var rig = new HeadRig();

        await rig.Service.SyncAsync(CancellationToken.None);
        await rig.Service.SyncAsync(CancellationToken.None);

        var beats = LeaseView.Read(rig.Opened()).Heartbeats;
        Assert.Equal(RosterRole.Owner, beats[rig.Service.InstallId].Role);
    }

    [Fact]
    public async Task AnUnknownRoleWritesNoHeartbeat()
    {
        using var owner = new HeadRig();
        await Seed(owner);
        await owner.Service.SyncAsync(CancellationToken.None);
        using var unknown = new HeadRig(owner.Drive, HouseholdFileRole.Unknown);

        var standing = await unknown.Service.SyncAsync(CancellationToken.None);
        await unknown.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(HouseholdFileState.Synced, standing.State);
        Assert.DoesNotContain(unknown.Service.InstallId, LeaseView.Read(await unknown.Local.LoadAsync(CancellationToken.None)).Heartbeats.Keys);
    }

    [Fact]
    public async Task APartnerHeartbeatNamesThePartner()
    {
        var owner = new HeadRig();
        await Seed(owner);
        await owner.Service.SyncAsync(CancellationToken.None);
        using var partner = new HeadRig(owner.Drive, HouseholdFileRole.SharedWith);

        await partner.Service.SyncAsync(CancellationToken.None);
        await partner.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(RosterRole.Partner, LeaseView.Read(await partner.Local.LoadAsync(CancellationToken.None)).Heartbeats[partner.Service.InstallId].Role);
        owner.Dispose();
    }

    [Fact]
    public async Task TheInstallKeepsItsIdAndItsClock()
    {
        using var rig = new HeadRig();
        await Seed(rig);
        var first = rig.Install.Load();

        var again = new FileHouseholdInstallStore(Path.GetDirectoryName(rig.Install.FilePath)!).Load();

        Assert.Equal(first.InstallId, again.InstallId);
        Assert.True(again.LastStamp > HlcTimestamp.Zero);
    }

    [Fact]
    public async Task ThePassphraseNeverLeavesTheKeeper()
    {
        using var rig = new HeadRig();
        await Seed(rig);
        rig.Drive.Failure = new IOException("transport down " + HeadRig.Passphrase);
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.Drive.Failure = null;
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.HeldPassphrase = HeadRig.OtherKey;
        await rig.Service.SyncAsync(CancellationToken.None);

        var said = string.Join("\n", rig.Log.Lines) + rig.Service.Standing.Sentence;

        Assert.DoesNotContain(HeadRig.Passphrase, said, StringComparison.Ordinal);
        Assert.DoesNotContain(HeadRig.OtherKey, said, StringComparison.Ordinal);
        Assert.DoesNotContain("transport down", said, StringComparison.Ordinal);
    }
}
