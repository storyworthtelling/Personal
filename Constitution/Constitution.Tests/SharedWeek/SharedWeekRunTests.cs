using Constitution.Domain.Delivery;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.Documents;
using Constitution.Tests.MealWeekDocuments;
using Constitution.Tests.MealWeekRun;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWeekDocuments.DocsData;
using static Constitution.Tests.SharedWeek.SharedWeekData;

namespace Constitution.Tests.SharedWeek;

internal sealed class FakePlans : IWeekPlanSource
{
    public WeekPlansRead Answer { get; set; } = Held();

    public int Reads { get; private set; }

    public static WeekPlansRead Held(params (SharedWeekPlan Plan, string Writer)[] plans) =>
        new WeekPlansRead.Held(new WeekPlanSet(plans.Select(item => new HeldWeekPlan(item.Plan, item.Writer)).ToList(), []), PartnerPhone);

    public WeekPlansRead Read()
    {
        Reads++;
        return Answer;
    }
}

/// <summary>
/// The other adult's phone: it holds no kept week of its own, reads the week the lease holder published, and sends her tablet what the same rules send.
/// Lee's tablet; Sunday's dinner is Lee's with the block from 5:15 pm, so its hour is 4:15 to 5:15.
/// </summary>
public class SharedWeekRunTests
{
    private static readonly DateTimeOffset Inside = Edt(10, 25, 16, 30);

    private readonly DocsRig docs = new();
    private readonly MealWeekRig weeks = new();
    private readonly FakePlans plans = new();

    public SharedWeekRunTests()
    {
        weeks.Box.Of(Cards());
        plans.Answer = FakePlans.Held((Plan(["Tuesday dinner"], Daily()), OwnerPhone));
    }

    private MealSharedSeams Seams() => new(plans, weeks.Box, weeks.Taste, docs.Seams, docs.Log);

    private async Task<MealRecipesOutcome> RunAsync(DateTimeOffset now, string person = Lee)
    {
        docs.Now = now;
        docs.Person = person;
        return await MealSharedWeekRun.RunAsync(new MealWeekRequestInputs(Sunday.AddDays(7), now, Rules()) { DeliverFor = [person] }, Seams(), CancellationToken.None);
    }

    private async Task<MealRecipesOutcome.Done> DoneAsync(DateTimeOffset now, string person = Lee) =>
        Assert.IsType<MealRecipesOutcome.Done>(await RunAsync(now, person));

    private static string SheetText(DocsRig docs) =>
        string.Join("\n", PdfReadBack.Open(docs.Tablet.PdfOf(docs.SheetTitle)).GetPages().Select(page => page.Text));

    [Fact]
    public async Task ARecipeInItsHourReachesHerTablet()
    {
        var done = await DoneAsync(Inside);

        var result = Assert.IsType<MealDocumentsResult.Done>(Assert.Single(done.Weeks).Documents);
        Assert.Equal(MealDocumentVerdict.Delivered, docs.RecipeOn(result, 0).Verdict);
        Assert.Contains(DocsRig.RecipeTitle(0, Lee), docs.Tablet.Titles.Values);
    }

    [Fact]
    public async Task PastTheDeadlineNoSheetIsSent()
    {
        var done = await DoneAsync(Inside);

        var result = Assert.IsType<MealDocumentsResult.Done>(Assert.Single(done.Weeks).Documents);
        Assert.All(result.Documents, item => Assert.Equal(DeliveryKind.Recipe, item.Kind));
        Assert.DoesNotContain(docs.Tablet.Titles.Values, title => title.Contains("Meals", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BeforeTheDeadlineTheSheetGoesWithTheTemplatePagesAndTheDeniedLine()
    {
        var done = await DoneAsync(Edt(10, 22, 12));

        var result = Assert.IsType<MealDocumentsResult.Done>(Assert.Single(done.Weeks).Documents);
        Assert.Equal(MealDocumentVerdict.Delivered, docs.Sheet(result).Verdict);
        Assert.Contains("Pancakes", SheetText(docs), StringComparison.Ordinal);
        Assert.Contains("Tuesday dinner", SheetText(docs), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSheetIsSentToHerTabletOnlyOnce()
    {
        await DoneAsync(Edt(10, 22, 12));

        var second = await RunAsync(Edt(10, 22, 13));

        Assert.IsType<MealRecipesOutcome.Nothing>(second);
        Assert.Equal(1, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task TheDeliveryIsRecordedForTheReadBackAndForTheNextLook()
    {
        await DoneAsync(Inside);

        var recorded = Assert.Single(docs.Expected.Recorded);
        Assert.Equal(DocsRig.RecipeTitle(0, Lee), recorded.Name);
        Assert.IsType<MealRecipesOutcome.Nothing>(await RunAsync(Inside.AddMinutes(30)));
        Assert.Equal(1, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task APageAlreadyOnTheTabletIsLeftAloneWhateverTheLeaseHolderRepublishes()
    {
        await DoneAsync(Edt(10, 22, 12));
        plans.Answer = FakePlans.Held((Plan(["Tuesday dinner"], Daily(), Edt(10, 22, 18)), OwnerPhone));

        var again = await RunAsync(Edt(10, 22, 18));

        Assert.IsType<MealRecipesOutcome.Nothing>(again);
        Assert.Equal(1, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task OnlyHerOwnDinnersGoToHerTablet()
    {
        var outcome = await RunAsync(Inside, Sam);

        Assert.IsType<MealRecipesOutcome.Nothing>(outcome);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task AWeekThisPhoneItselfPublishedIsNotItsToSend()
    {
        plans.Answer = FakePlans.Held((Plan(), PartnerPhone));

        var outcome = await RunAsync(Inside);

        Assert.IsType<MealRecipesOutcome.Nothing>(outcome);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task NothingDueReadsNeitherTheBoxNorTheTaste()
    {
        var outcome = await RunAsync(Edt(10, 25, 12));

        Assert.IsType<MealRecipesOutcome.Nothing>(outcome);
        Assert.DoesNotContain("recipes", weeks.Calls);
        Assert.DoesNotContain("taste", weeks.Calls);
        Assert.Empty(docs.Log.Phrases.Where(line => line.Contains("none due", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NothingIsPlannedPlacedOrSaved()
    {
        await DoneAsync(Inside);

        Assert.DoesNotContain("lease", weeks.Calls);
        Assert.DoesNotContain("calendars", weeks.Calls);
        Assert.DoesNotContain("history", weeks.Calls);
        Assert.DoesNotContain("records", weeks.Calls);
        weeks.AssertNothingWritten();
    }

    [Fact]
    public async Task AnUnreadableHouseholdFileStopsItWithTheFilesOwnSentence()
    {
        plans.Answer = new WeekPlansRead.Blocked("The household file cannot be opened.");

        var outcome = await RunAsync(Inside);

        var stopped = Assert.IsType<MealRecipesOutcome.Stopped>(outcome);
        Assert.Equal(MealRecipesStoppedBecause.WeeksUnreadable, stopped.Because);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task AnUnreadableBoxStopsItAndSendsNothing()
    {
        weeks.Box.Answer = new RecipeBoxRead.Unavailable("The box cannot be read.");

        var outcome = await RunAsync(Inside);

        Assert.Equal(MealRecipesStoppedBecause.RecipesUnavailable, Assert.IsType<MealRecipesOutcome.Stopped>(outcome).Because);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task AnUnreadableTasteProfileStopsItAndSendsNothing()
    {
        weeks.Taste.Answer = new MealTasteRead.Unreadable("The taste profile cannot be read.");

        var outcome = await RunAsync(Inside);

        Assert.Equal(MealRecipesStoppedBecause.TasteUnreadable, Assert.IsType<MealRecipesOutcome.Stopped>(outcome).Because);
        Assert.Equal(0, docs.Tablet.Uploads);
    }

    [Fact]
    public async Task ATabletThatRefusesIsTheStepsNotDeliveredAndTheNextLookTriesAgain()
    {
        docs.Tablet.UploadFails = new HttpRequestException("The tablet is not answering.");
        var first = await DoneAsync(Inside);
        var result = Assert.IsType<MealDocumentsResult.Done>(Assert.Single(first.Weeks).Documents);
        Assert.Equal(MealDocumentVerdict.NotDelivered, docs.RecipeOn(result, 0).Verdict);

        docs.Tablet.UploadFails = null;
        var second = await DoneAsync(Inside.AddMinutes(30));

        Assert.Equal(MealDocumentVerdict.Delivered, docs.RecipeOn(Assert.IsType<MealDocumentsResult.Done>(Assert.Single(second.Weeks).Documents), 0).Verdict);
    }
}
