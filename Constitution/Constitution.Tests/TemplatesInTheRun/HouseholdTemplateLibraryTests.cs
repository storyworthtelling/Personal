using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Infrastructure.Household;
using Constitution.Tests.HouseholdHead;
using Constitution.Tests.Templates;
using Storyworthtelling.Household;
using static Constitution.Tests.TemplatesInTheRun.TemplateRunData;

namespace Constitution.Tests.TemplatesInTheRun;

/// <summary>The library over the household file, with the same invented phones the recipe box tests use.</summary>
public class HouseholdTemplateLibraryTests
{
    private static HouseholdTemplateLibrary Library(HeadRig rig) => new(rig.Service);

    private static TemplateLibraryRead.Held Held(TemplateLibraryRead read) => Assert.IsType<TemplateLibraryRead.Held>(read);

    [Fact]
    public void ANewPhoneHasNoLibraryYet()
    {
        using var rig = new HeadRig();

        var none = Assert.IsType<TemplateLibraryRead.NoneYet>(Library(rig).Load());

        Assert.False(string.IsNullOrWhiteSpace(none.Sentence));
    }

    [Fact]
    public async Task AHouseholdWithNoTemplatesHasAnEmptyLibrary()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);

        var held = Held(Library(rig).Load());

        Assert.True(held.Library.IsEmpty);
        Assert.Equal(0, held.UnreadableRecords);
    }

    [Fact]
    public async Task ASavedLibraryComesBackAsItWas()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var library = Full().WithPrints([new TemplatePrint(Week, "box-fruit-yes", "tpl-box-fruit-oats")]);

        var saved = Library(rig).Save(library);

        Assert.IsType<TemplateSave.Saved>(saved);
        var back = Held(Library(rig).Load()).Library;
        Assert.True(TemplateRunRules.Same(library, back));
    }

    [Fact]
    public async Task SavingTheSameLibraryTwiceChangesNothing()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var store = Library(rig);
        store.Save(Full());
        var once = await rig.Local.LoadAsync(CancellationToken.None);

        store.Save(Full());

        var twice = await rig.Local.LoadAsync(CancellationToken.None);
        Assert.True(twice.ContentEquals(once));
    }

    [Fact]
    public async Task OnlyTemplateRecordsAreWritten()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var before = (await rig.Local.LoadAsync(CancellationToken.None)).Records.Count;

        Library(rig).Save(Full());

        var document = await rig.Local.LoadAsync(CancellationToken.None);
        var added = document.Records.Where(record => TemplateKeys.IsTemplate(record.Key) || TemplateKeys.IsPrint(record.Key)).Count();
        Assert.Equal(27, added);
        Assert.Equal(before + 27, document.Records.Count);
    }

    [Fact]
    public async Task APartnerReadsWhatTheOwnerSaved()
    {
        using var owner = new HeadRig();
        await owner.Service.SyncAsync(CancellationToken.None);
        Library(owner).Save(Full());
        await owner.Service.SyncAsync(CancellationToken.None);
        using var partner = new HeadRig(owner.Drive, HouseholdFileRole.SharedWith);

        await partner.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(27, Held(Library(partner).Load()).Library.Templates.Count);
    }

    [Fact]
    public async Task AnUnreadableFileIsTypedAndNothingIsWritten()
    {
        using var rig = new HeadRig();
        using var theirs = new HeadRig();
        rig.Drive.Content = theirs.SealedFile(HouseholdDocument.Empty, "some-other-passphrase");
        await rig.Service.SyncAsync(CancellationToken.None);
        var before = rig.Drive.Content;

        var read = Assert.IsType<TemplateLibraryRead.Unreadable>(Library(rig).Load());
        var saved = Assert.IsType<TemplateSave.Refused>(Library(rig).Save(Full()));

        Assert.Contains("passphrase", read.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(read.Reason, saved.Sentence);
        Assert.Equal(before, rig.Drive.Content);
        Assert.Empty((await rig.Local.LoadAsync(CancellationToken.None)).Records);
    }

    [Fact]
    public async Task ADamagedLocalCopyIsUnreadableAndUntouched()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var path = Path.Combine(Path.GetDirectoryName(rig.Install.FilePath)!, "household.json");
        await File.WriteAllTextAsync(path, "{ damaged");

        var read = Library(rig).Load();
        var saved = Library(rig).Save(Full());

        Assert.IsType<TemplateLibraryRead.Unreadable>(read);
        Assert.IsType<TemplateSave.Refused>(saved);
        Assert.Equal("{ damaged", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ARecordThisBuildCannotReadIsCountedAndKept()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var template = TemplateData.Approved("tpl-box-fruit-oats", MealTemplateSlot.BoxFruit, "Grapes");
        var fields = TemplateMaps.Template.FieldsOf(template).ToDictionary(pair => pair.Key, pair => pair.Value);
        fields["status"] = "a-newer-word";
        var odd = HouseholdRecord.Write(TemplateMaps.Template.KeyOf(template), 1, fields, new HlcTimestamp(1_000, 0), "install-newer");
        await rig.Service.EditAsync((HouseholdDocument document, HybridLogicalClock clock, string writer) => document.With(odd), CancellationToken.None);

        var held = Held(Library(rig).Load());

        Assert.Equal(1, held.UnreadableRecords);
        Assert.Empty(held.Library.Templates);
        Assert.Contains("1 records unreadable", rig.Log.Lines);
    }
}
