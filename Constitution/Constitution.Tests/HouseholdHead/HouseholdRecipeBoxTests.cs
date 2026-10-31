using Constitution.Domain.Diagnostics;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Household;
using Constitution.Tests.Recipes;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdHead;

public class HouseholdRecipeBoxTests
{
    private static RecipeBoxRead.Ready Ready(RecipeBoxRead read) => Assert.IsType<RecipeBoxRead.Ready>(read);

    [Fact]
    public async Task AKeptCardRoundTripsIntoTheBox()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);

        var result = rig.Keeper.Keep(RecipeData.Chili());

        Assert.True(result.IsKept);
        var card = Assert.Single(Ready(rig.Box.Read()).Snapshot.Cards);
        Assert.Equal(RecipeData.Chili(), card);
    }

    [Fact]
    public async Task AKeptCardReachesThePartnersBox()
    {
        using var owner = new HeadRig();
        await owner.Service.SyncAsync(CancellationToken.None);
        owner.Keeper.Keep(RecipeData.Chili());
        await owner.Service.SyncAsync(CancellationToken.None);
        using var partner = new HeadRig(owner.Drive, HouseholdFileRole.SharedWith);

        await partner.Service.SyncAsync(CancellationToken.None);

        Assert.Single(Ready(partner.Box.Read()).Snapshot.Cards);
    }

    [Fact]
    public void NoPassphraseSaysSoInTheBox()
    {
        using var rig = new HeadRig { HeldPassphrase = string.Empty };
        rig.Service.SyncAsync(CancellationToken.None).GetAwaiter().GetResult();

        var read = rig.Box.Read();

        var none = Assert.IsType<RecipeBoxRead.NoHouseholdFile>(read);
        Assert.Contains("passphrase", none.Sentence, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(NoRecipeBoxSource.Sentence, none.Sentence);
    }

    [Fact]
    public void NothingComparedYetSaysSo()
    {
        using var rig = new HeadRig();

        var none = Assert.IsType<RecipeBoxRead.NoHouseholdFile>(rig.Box.Read());

        Assert.Contains("not been compared", none.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWaitingShareSaysSoInTheBox()
    {
        using var rig = new HeadRig(role: HouseholdFileRole.SharedWith);
        await rig.Service.SyncAsync(CancellationToken.None);

        var none = Assert.IsType<RecipeBoxRead.NoHouseholdFile>(rig.Box.Read());

        Assert.Contains(HeadRig.FileName, none.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableFileLeavesTheSpecificSentence()
    {
        using var rig = new HeadRig();
        using var theirs = new HeadRig();
        rig.Drive.Content = theirs.SealedFile(RecipeData.Document([RecipeData.Chili()]), "some-other-passphrase");
        await rig.Service.SyncAsync(CancellationToken.None);
        var before = rig.Drive.Content;

        var unavailable = Assert.IsType<RecipeBoxRead.Unavailable>(rig.Box.Read());
        var refused = rig.Keeper.Keep(RecipeData.Chili());

        Assert.Contains("passphrase", unavailable.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not open", unavailable.Reason, StringComparison.Ordinal);
        Assert.False(refused.IsKept);
        Assert.Equal(unavailable.Reason, refused.Refusal);
        Assert.Equal(before, rig.Drive.Content);
        Assert.Empty((await rig.Local.LoadAsync(CancellationToken.None)).Records);
    }

    [Fact]
    public async Task ANewerFormatSaysItIsNewer()
    {
        using var rig = new HeadRig();
        using var theirs = new HeadRig();
        var future = HouseholdDocumentCodec.Serialize(RecipeData.Document([RecipeData.Chili()])).Replace("\"version\":1", "\"version\":99");
        rig.Drive.Content = Storyworthtelling.Voice.Secrets.VaultEnvelope.Write(future, HeadRig.Passphrase);
        await rig.Service.SyncAsync(CancellationToken.None);

        var state = rig.Service.Standing;

        if (state.State == HouseholdFileState.Unreadable)
        {
            Assert.Contains("newer", Assert.IsType<RecipeBoxRead.Unavailable>(rig.Box.Read()).Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ADamagedLocalCopySaysSoInTheBox()
    {
        using var rig = new HeadRig();
        rig.Keeper.Keep(RecipeData.Chili());
        var path = Path.Combine(Path.GetDirectoryName(rig.Install.FilePath)!, "household.json");
        await File.WriteAllTextAsync(path, "{ damaged");

        var unavailable = Assert.IsType<RecipeBoxRead.Unavailable>(rig.Box.Read());

        Assert.Contains("damaged", unavailable.Reason, StringComparison.Ordinal);
        Assert.Equal("{ damaged", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AMoveIsKeptAndPublished()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var chili = RecipeData.Chili();
        rig.Keeper.Keep(chili);
        var move = new CuisineMove(chili.Id, chili.Cuisine, Cuisine.Asian);

        rig.Box.Move(move);
        await rig.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(Cuisine.Asian, Assert.Single(Ready(rig.Box.Read()).Snapshot.Cards).Cuisine);
        Assert.Equal(Cuisine.Asian, Assert.Single(HouseholdRecords.Read(rig.Opened(), RecipeMaps.Recipe).Items).Cuisine);
    }

    [Fact]
    public async Task AMoveUndoneRestoresTheCard()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        var chili = RecipeData.Chili();
        rig.Keeper.Keep(chili);
        var move = new CuisineMove(chili.Id, chili.Cuisine, Cuisine.Asian);

        rig.Box.Move(move);
        rig.Box.Move(move.Inverse);

        Assert.Equal(chili.Cuisine, Assert.Single(Ready(rig.Box.Read()).Snapshot.Cards).Cuisine);
    }

    [Fact]
    public void AMoveOfAGoneCardIsInvalid()
    {
        using var rig = new HeadRig();
        var move = new CuisineMove(new RecipeId("nobody-kept-this"), Cuisine.Other, Cuisine.Asian);

        var failure = Assert.Throws<InvalidOperationException>(() => rig.Box.Move(move));

        Assert.Equal(CuisineMove.CardGoneSentence, failure.Message);
    }

    [Fact]
    public async Task AnUnreadableCardIsLeftOutAndCounted()
    {
        using var rig = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.Keeper.Keep(RecipeData.Chili());
        await rig.Service.EditAsync((document, stamp, writer) => document.With(
            HouseholdRecord.Write(new RecordKey(ConstitutionKinds.Recipe, "broken-card"), 1, new Dictionary<string, string> { ["recipe"] = "broken-card" }, stamp, writer)), CancellationToken.None);

        var read = Ready(rig.Box.Read());

        Assert.Single(read.Snapshot.Cards);
        Assert.Contains(rig.Log.Lines, line => line == "1 records unreadable");
    }

    [Fact]
    public void ThePlannerIsStillAbsent()
    {
        using var rig = new HeadRig();

        Assert.Equal(CookSoonResult.NoPlannerYet, rig.Box.PlaceNextWeek(new RecipeId("any-card")));
    }

    [Theory]
    [InlineData(HouseholdFileRole.Owner, InstallRole.Owner)]
    [InlineData(HouseholdFileRole.SharedWith, InstallRole.Partner)]
    [InlineData(HouseholdFileRole.Unknown, InstallRole.Unknown)]
    public void TheFileRoleIsThePhonesRole(HouseholdFileRole file, InstallRole install) =>
        Assert.Equal(install, HouseholdInstallRoles.For(file));
}
