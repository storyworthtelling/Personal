using Constitution.Domain.Household;
using Constitution.Infrastructure.Household;
using Constitution.Tests.HouseholdHead;
using Storyworthtelling.Household;

namespace Constitution.Tests.PersonSheet;

public class HouseholdRosterChangesTests
{
    private static async Task Hold(HeadRig rig, HouseholdDocument document)
    {
        await rig.Local.SaveAsync(document, CancellationToken.None);
        await rig.Service.ReadAsync(CancellationToken.None);
    }

    private static async Task<HouseholdDocument> Kept(HeadRig rig) => await rig.Local.LoadAsync(CancellationToken.None);

    private static IEnumerable<string> Names(HeadRig rig) => rig.Service.Roster!.Entries.Select(entry => entry.Name);

    [Fact]
    public async Task RemovingGoesThroughTheServiceAtOnce()
    {
        using var rig = new HeadRig();
        await Hold(rig, SheetData.Full());

        var result = await new HouseholdRosterChanges(rig.Service).RemoveAsync(SheetData.Robin, CancellationToken.None);

        Assert.True(result.Done);
        Assert.Null(result.Sentence);
        Assert.Equal("Robin", result.Removed!.Name);
        Assert.DoesNotContain("Robin", Names(rig));
        Assert.True((await Kept(rig)).Find(SheetData.RobinsRating)!.IsTombstone);
    }

    [Fact]
    public async Task UndoWritesThePersonBackNewer()
    {
        using var rig = new HeadRig();
        await Hold(rig, SheetData.Full());
        var changes = new HouseholdRosterChanges(rig.Service);
        var removed = (await changes.RemoveAsync(SheetData.Robin, CancellationToken.None)).Removed!;
        var tombstone = (await Kept(rig)).Find(new Storyworthtelling.Household.RecordKey(ConstitutionKinds.Person, "robin"))!;

        var result = await changes.RestoreAsync(removed, CancellationToken.None);

        Assert.True(result.Done);
        Assert.Contains("Robin", Names(rig));
        Assert.True((await Kept(rig)).Live(tombstone.Key)!.Stamp.CompareTo(tombstone.Stamp) > 0);
    }

    [Fact]
    public async Task TheLastAdultIsRefusedAndNothingIsWritten()
    {
        using var rig = new HeadRig();
        await Hold(rig, SheetData.OneAdult());
        var before = await Kept(rig);

        var result = await new HouseholdRosterChanges(rig.Service).RemoveAsync(SheetData.Sam, CancellationToken.None);

        Assert.False(result.Done);
        Assert.Equal(RosterRemoval.LastAdultSentence("Sam"), result.Sentence);
        Assert.True((await Kept(rig)).ContentEquals(before));
    }

    [Fact]
    public async Task ThePartnerPhoneWritesNothing()
    {
        using var rig = new HeadRig(role: HouseholdFileRole.SharedWith);
        await Hold(rig, SheetData.Full());
        var before = await Kept(rig);

        var removed = await new HouseholdRosterChanges(rig.Service).RemoveAsync(SheetData.Robin, CancellationToken.None);
        var saved = await new HouseholdRosterChanges(rig.Service).SaveAsync(SheetData.Robin, "Robin R", false, null, CancellationToken.None);

        Assert.False(removed.Done);
        Assert.Equal(RosterEditor.PartnerReadOnlySentence, removed.Sentence);
        Assert.False(saved.Done);
        Assert.True((await Kept(rig)).ContentEquals(before));
        Assert.Equal(0, rig.Drive.Writes);
    }

    [Fact]
    public async Task AnUnknownRoleMayRemove()
    {
        using var rig = new HeadRig(role: HouseholdFileRole.Unknown);
        await Hold(rig, SheetData.Full());

        var result = await new HouseholdRosterChanges(rig.Service).RemoveAsync(SheetData.Robin, CancellationToken.None);

        Assert.True(result.Done);
    }

    [Fact]
    public async Task AFileThatWillNotOpenIsNeverWrittenOver()
    {
        using var rig = new HeadRig();
        await Hold(rig, SheetData.Full());
        rig.Drive.Content = "this is not an envelope";
        await rig.Service.SyncAsync(CancellationToken.None);
        var before = await Kept(rig);

        var removed = await new HouseholdRosterChanges(rig.Service).RemoveAsync(SheetData.Robin, CancellationToken.None);
        var saved = await new HouseholdRosterChanges(rig.Service).SaveAsync(SheetData.Sam, "Sam S", true, null, CancellationToken.None);

        Assert.False(removed.Done);
        Assert.Equal(rig.Service.BlockedSentence, removed.Sentence);
        Assert.False(saved.Done);
        Assert.Equal(rig.Service.BlockedSentence, saved.Sentence);
        Assert.True((await Kept(rig)).ContentEquals(before));
        Assert.Equal("this is not an envelope", rig.Drive.Content);
        Assert.Equal(0, rig.Drive.Writes);
    }

    [Fact]
    public async Task SavingKeepsTheBirthdateAndSaysNothingWentWrong()
    {
        using var rig = new HeadRig();
        await Hold(rig, SheetData.Full());

        var result = await new HouseholdRosterChanges(rig.Service).SaveAsync(SheetData.Robin, "Robin R", false, null, CancellationToken.None);

        Assert.True(result.Done);
        Assert.Contains("Robin R", Names(rig));
        Assert.Equal(new Birthdate(new DateOnly(1900, 3, 3)), SheetData.People(await Kept(rig)).Single(person => person.Id == SheetData.Robin).Birthdate);
    }

    [Fact]
    public async Task ABadSaveCarriesTheProblemsAsASentence()
    {
        using var rig = new HeadRig();
        await Hold(rig, SheetData.Full());

        var result = await new HouseholdRosterChanges(rig.Service).SaveAsync(SheetData.Sam, "Sam", true, "not an address", CancellationToken.None);

        Assert.False(result.Done);
        Assert.Contains("address", result.Sentence!, StringComparison.OrdinalIgnoreCase);
    }
}
