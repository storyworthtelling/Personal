using Constitution.Domain.Delivery;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.MealWeekDocuments;
using Constitution.Tests.MealWeekRun;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWeekDocuments.DocsData;

namespace Constitution.Tests.RecipeHour;

/// <summary>
/// The documents-only entry for the week in progress: the kept week, the recipe box and the same documents step, with no planner, no calendar and no sheet.
/// Lee's phone. Sunday's dinner is Lee's with the block from 5:15 pm, so its hour is 4:15 to 5:15; Monday's is Sam's.
/// </summary>
public class RecipeHourRunTests
{
    private static readonly DateTimeOffset Inside = Edt(10, 25, 16, 20);

    private readonly DocsRig docs = new();
    private readonly MealWeekRig weeks = new();

    public RecipeHourRunTests()
    {
        weeks.Box.Of(Cards());
        weeks.Records.Json = MealWeekRecordCodec.Encode(new MealWeekRecord([new StoredMealWeek(Week(), Edt(10, 21, 12), [])]));
    }

    private MealRecipeSeams Seams() => new(weeks.Records, weeks.Box, weeks.Taste, docs.Seams, docs.Log);

    private MealWeekRequestInputs Inputs(DateTimeOffset now, params string[] deliverFor) =>
        new(Sunday.AddDays(7), now, Rules()) { DeliverFor = deliverFor.Length == 0 ? [Lee] : deliverFor };

    private async Task<MealRecipesOutcome> RunAsync(DateTimeOffset now, params string[] deliverFor)
    {
        docs.Now = now;
        return await MealRecipeHourRun.RunAsync(Inputs(now, deliverFor), Seams(), CancellationToken.None);
    }

    private async Task<MealRecipesOutcome.Done> DoneAsync(DateTimeOffset now, params string[] deliverFor) =>
        Assert.IsType<MealRecipesOutcome.Done>(await RunAsync(now, deliverFor));

    // ---- sending ----

    [Fact]
    public async Task TheWeekInProgressGetsItsRecipeInItsHour()
    {
        var done = await DoneAsync(Inside);

        var week = Assert.Single(done.Weeks);
        Assert.Equal(Sunday, week.Week);
        var result = Assert.IsType<MealDocumentsResult.Done>(week.Documents);
        Assert.Equal(MealDocumentVerdict.Delivered, docs.RecipeOn(result, 0).Verdict);
        Assert.Contains(DocsRig.RecipeTitle(0, Lee), docs.Tablet.Titles.Values);
    }

    [Fact]
    public async Task TheSheetIsNeverBuiltOrSent()
    {
        docs.Pantry.Answer = new MealPantryRead.Unreadable("The pantry cannot be opened.");

        var done = await DoneAsync(Inside);

        var result = Assert.IsType<MealDocumentsResult.Done>(Assert.Single(done.Weeks).Documents);
        Assert.All(result.Documents, item => Assert.Equal(DeliveryKind.Recipe, item.Kind));
        Assert.DoesNotContain(docs.Tablet.Titles.Values, title => title.Contains("Meals", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NothingIsPlannedPlacedOrChanged()
    {
        var before = weeks.Records.Json;

        await DoneAsync(Inside);

        Assert.Equal(before, weeks.Records.Json);
        Assert.Equal(0, weeks.Records.Saves);
        Assert.DoesNotContain("lease", weeks.Calls);
        Assert.DoesNotContain("calendars", weeks.Calls);
        Assert.DoesNotContain("history", weeks.Calls);
        weeks.AssertNothingWritten();
    }

    [Fact]
    public async Task TheDeliveryIsRecordedSoTheNextLookFindsNothingDue()
    {
        await DoneAsync(Inside);
        var second = await RunAsync(Inside.AddMinutes(30));

        Assert.IsType<MealRecipesOutcome.Nothing>(second);
        Assert.Equal(1, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task ThePageIsRecordedForTheReadBack()
    {
        await DoneAsync(Inside);

        var recorded = Assert.Single(docs.Expected.Recorded);
        Assert.Equal(DeliveryKind.Recipe, recorded.Kind);
        Assert.Equal(DocsRig.RecipeTitle(0, Lee), recorded.Name);
    }

    // ---- when nothing is due ----

    [Fact]
    public async Task OutsideTheHourNothingIsReadOrSent()
    {
        var outcome = await RunAsync(Edt(10, 25, 12));

        Assert.IsType<MealRecipesOutcome.Nothing>(outcome);
        Assert.DoesNotContain("recipes", weeks.Calls);
        Assert.DoesNotContain("taste", weeks.Calls);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task OnceTheBlockHasBegunNothingIsSent()
    {
        var outcome = await RunAsync(Edt(10, 25, 17, 15));

        Assert.IsType<MealRecipesOutcome.Nothing>(outcome);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task TheOtherCooksRecipeIsNotSentHere()
    {
        var outcome = await RunAsync(Edt(10, 26, 16, 20));

        Assert.IsType<MealRecipesOutcome.Nothing>(outcome);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task NoSavedWeekIsNothingDue()
    {
        weeks.Records.Json = null;

        Assert.IsType<MealRecipesOutcome.Nothing>(await RunAsync(Inside));
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    // ---- typed stops ----

    [Fact]
    public async Task UnreadableWeeksAreATypedStop()
    {
        weeks.Records.Json = "this is not a saved week";

        var stopped = Assert.IsType<MealRecipesOutcome.Stopped>(await RunAsync(Inside));

        Assert.Equal(MealRecipesStoppedBecause.WeeksUnreadable, stopped.Because);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task AnUnavailableBoxIsATypedStop()
    {
        weeks.Box.Answer = new RecipeBoxRead.Unavailable("The household file cannot be opened.");

        var stopped = Assert.IsType<MealRecipesOutcome.Stopped>(await RunAsync(Inside));

        Assert.Equal(MealRecipesStoppedBecause.RecipesUnavailable, stopped.Because);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task NoHouseholdFileIsATypedStopToo()
    {
        weeks.Box.Answer = new RecipeBoxRead.NoHouseholdFile("There is no household file yet.");

        var stopped = Assert.IsType<MealRecipesOutcome.Stopped>(await RunAsync(Inside));

        Assert.Equal(MealRecipesStoppedBecause.RecipesUnavailable, stopped.Because);
    }

    [Fact]
    public async Task AnUnreadableTasteProfileIsATypedStop()
    {
        weeks.Taste.Answer = new MealTasteRead.Unreadable("The taste profile cannot be read.");

        var stopped = Assert.IsType<MealRecipesOutcome.Stopped>(await RunAsync(Inside));

        Assert.Equal(MealRecipesStoppedBecause.TasteUnreadable, stopped.Because);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task EveryStopSaysItsReasonAndLogsOneLine()
    {
        weeks.Box.Answer = new RecipeBoxRead.Unavailable("The household file cannot be opened.");

        await RunAsync(Inside);

        Assert.Equal([MealRecipesTrail.LogPhrase(new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.RecipesUnavailable, "x"))], docs.Log.Phrases);
    }

    // ---- a tablet that will not answer ----

    [Fact]
    public async Task ARefusedPageIsReportedNotThrownAndTriedAgainByTheNextLook()
    {
        docs.Tablet.UploadFails = new HttpRequestException("The tablet is not answering.");

        var failed = await DoneAsync(Inside);

        var result = Assert.IsType<MealDocumentsResult.Done>(Assert.Single(failed.Weeks).Documents);
        Assert.Equal(MealDocumentVerdict.NotDelivered, docs.RecipeOn(result, 0).Verdict);
        Assert.Empty(docs.Delivered.Held);

        docs.Tablet.UploadFails = null;
        var mended = await DoneAsync(Inside.AddMinutes(30));

        Assert.Equal(MealDocumentVerdict.Delivered, docs.RecipeOn(Assert.IsType<MealDocumentsResult.Done>(Assert.Single(mended.Weeks).Documents), 0).Verdict);
    }

    [Fact]
    public async Task NoTabletForThisPhoneIsNothingDue()
    {
        docs.Now = Inside;

        var outcome = await MealRecipeHourRun.RunAsync(new MealWeekRequestInputs(Sunday.AddDays(7), Inside, Rules()), Seams(), CancellationToken.None);

        Assert.IsType<MealRecipesOutcome.Nothing>(outcome);
    }

    [Fact]
    public async Task ACancelledCallerIsNotATypedOutcome()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MealRecipeHourRun.RunAsync(Inputs(Inside), Seams(), source.Token));
    }

    // ---- the trail ----

    [Fact]
    public async Task TheStepWritesItsOwnLinesAndTheRunAddsNoneOfItsOwnWhenItSends()
    {
        await DoneAsync(Inside);

        Assert.Equal(["meal documents: 1 documents delivered", "meal documents: 2 documents not yet", "meal documents: 3 documents not here"], docs.Log.Phrases);
    }
}
