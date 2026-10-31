using Constitution.Domain.Delivery;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Groceries;
using Constitution.Domain.Ink;
using Constitution.Infrastructure.Meals;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Notices;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts.Pipeline;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealWeekDocuments.DocsData;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>The documents step of the weekly meal run: the sheet to this phone's tablet, each recipe to its cook's, and a record for the read-back only once the tablet has the page.</summary>
public class MealDocumentsStepTests
{
    [Fact]
    public async Task TheSheetGoesToThisTablet()
    {
        var rig = new DocsRig();

        var done = await rig.RunAsync();

        var sheet = rig.Sheet(done);
        Assert.Equal(MealDocumentVerdict.Delivered, sheet.Verdict);
        Assert.Equal(rig.SheetTitle, sheet.Name);
        Assert.Equal(Lee, sheet.Person);
        Assert.Equal(Sunday, sheet.Day);
        Assert.Contains(rig.SheetTitle, rig.Tablet.Titles.Values);
        Assert.Equal(1, rig.Tablet.Uploads);
    }

    [Fact]
    public async Task TheSheetIsRecordedForTheReadBack()
    {
        var rig = new DocsRig();

        await rig.RunAsync();

        var expected = Assert.Single(rig.Expected.Recorded);
        Assert.Equal(rig.SheetTitle, expected.Name);
        Assert.Equal(DeliveryKind.Meals, expected.Kind);
        Assert.Equal(Lee, expected.Person);
        Assert.Equal(MealDeadline.ForWeek(Sunday, Zone), expected.Pages[0].Rules.OfType<MealItemRule>().First().Deadline);
        Assert.Equal(7, expected.Pages[0].Rules.Count);
        Assert.Contains(rig.SheetTitle, rig.Delivered.Held.Keys);
    }

    [Fact]
    public async Task TheSheetCarriesTheIdsItWillBeAnsweredUnder()
    {
        var rig = new DocsRig();

        await rig.RunAsync();

        var rules = rig.Expected.Recorded[0].Pages[0].Rules.OfType<MealItemRule>().ToList();
        Assert.Equal(
            ["dinner-sun-yes", "dinner-mon-yes", "dinner-tue-yes", "dinner-wed-yes", "dinner-thu-yes", "dinner-fri-yes", "dinner-sat-yes"],
            rules.Select(rule => rule.YesBoxId));
    }

    [Fact]
    public async Task ARecipeForTheOtherCookIsNotSentHere()
    {
        var rig = new DocsRig { Now = Edt(10, 26, 16, 30) };

        var done = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.NotHere, rig.RecipeOn(done, 3).Verdict);
        Assert.DoesNotContain(rig.Tablet.Titles.Values, title => title.Contains("Sam", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARecipeWaitsUntilAnHourBeforeItsBlock()
    {
        var rig = new DocsRig { Now = Edt(10, 25, 16, 14) };

        var done = await rig.RunAsync();

        var recipe = rig.RecipeOn(done, 0);
        Assert.Equal(MealDocumentVerdict.NotYet, recipe.Verdict);
        Assert.Equal(Edt(10, 25, 16, 15), recipe.At);
        Assert.DoesNotContain(rig.Tablet.Titles.Values, title => title.Contains("Recipe", StringComparison.Ordinal));
        Assert.Empty(rig.Expected.Recorded.Where(item => item.Kind == DeliveryKind.Recipe));
    }

    [Fact]
    public async Task TheCooksRecipeGoesOutAnHourBefore()
    {
        var rig = new DocsRig { Now = Edt(10, 25, 16, 15) };

        var done = await rig.RunAsync();

        var recipe = rig.RecipeOn(done, 0);
        Assert.Equal(MealDocumentVerdict.Delivered, recipe.Verdict);
        Assert.Equal(DocsRig.RecipeTitle(0, Lee), recipe.Name);
        Assert.Contains(DocsRig.RecipeTitle(0, Lee), rig.Tablet.Titles.Values);
    }

    [Fact]
    public async Task ARecipeIsNeverSentOnceItsBlockBegins()
    {
        var rig = new DocsRig { Now = Edt(10, 25, 17, 15) };

        var done = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.TooLate, rig.RecipeOn(done, 0).Verdict);
        Assert.DoesNotContain(rig.Tablet.Titles.Values, title => title.Contains("Recipe", StringComparison.Ordinal));
        Assert.Empty(rig.Expected.Recorded.Where(item => item.Kind == DeliveryKind.Recipe));
    }

    [Fact]
    public async Task TheBlockOnTheCalendarCountsWhenItIsBeforeTheFirstStep()
    {
        // The planner holds a block to 30 minutes at least; a 20 minute card starts at 5:40 but the block, and so the hour, is from 5:30.
        var quick = Card("veggie-fried-rice", "Veggie fried rice") with { Steps = [new RecipeStep("Stir-fry.", 20, true)] };
        var week = MealWeek.Planned(Sunday, [Cooked(0, "veggie-fried-rice", Lee, 17, 30), .. Week().Dinners.Skip(1)]);
        var early = new DocsRig { Now = Edt(10, 25, 16, 29), Week = week };
        early.Box = [quick, .. Cards().Skip(1)];
        var due = new DocsRig { Now = Edt(10, 25, 16, 30), Week = week };
        due.Box = [quick, .. Cards().Skip(1)];
        var late = new DocsRig { Now = Edt(10, 25, 17, 30), Week = week };
        late.Box = [quick, .. Cards().Skip(1)];

        Assert.Equal(MealDocumentVerdict.NotYet, early.RecipeOn(await early.RunAsync(), 0).Verdict);
        Assert.Equal(MealDocumentVerdict.Delivered, due.RecipeOn(await due.RunAsync(), 0).Verdict);
        Assert.Equal(MealDocumentVerdict.TooLate, late.RecipeOn(await late.RunAsync(), 0).Verdict);
    }

    [Fact]
    public async Task TheRecipeIsRecordedForTheReadBackWithItsRatingPage()
    {
        var rig = new DocsRig { Now = Edt(10, 25, 16, 30) };

        await rig.RunAsync();

        var recipe = Assert.Single(rig.Expected.Recorded, item => item.Kind == DeliveryKind.Recipe);
        Assert.Equal(DocsRig.RecipeTitle(0, Lee), recipe.Name);
        Assert.Equal(Lee, recipe.Person);
        var page = Assert.Single(recipe.Pages);
        Assert.Equal(3, page.Number);
        Assert.Equal(4, page.Rules.OfType<RatingRule>().Count());
    }

    [Fact]
    public async Task OnlyTheCooksTabletGetsItWhenThePhoneIsHers()
    {
        var rig = new DocsRig { Person = Sam, Now = Edt(10, 26, 16, 30) };

        var done = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.Delivered, rig.RecipeOn(done, 1).Verdict);
        Assert.Equal(MealDocumentVerdict.NotHere, rig.RecipeOn(done, 0).Verdict);
        Assert.Equal(MealDocumentVerdict.NotHere, rig.RecipeOn(done, 2).Verdict);
        Assert.Equal(DocsRig.RecipeTitle(1, Sam), rig.Expected.Recorded.Single(item => item.Kind == DeliveryKind.Recipe).Name);
    }

    [Fact]
    public async Task AFailedUploadRecordsNothing()
    {
        var rig = new DocsRig();
        rig.Tablet.UploadFails = new HttpRequestException("The cloud said no.");

        var done = await rig.RunAsync();

        var sheet = rig.Sheet(done);
        Assert.Equal(MealDocumentVerdict.NotDelivered, sheet.Verdict);
        Assert.IsType<HttpRequestException>(sheet.Failure);
        Assert.Empty(rig.Expected.Recorded);
        Assert.Equal(0, rig.Expected.Records);
        Assert.Equal(0, rig.Delivered.Records);
    }

    [Fact]
    public async Task AFailedUploadRaisesTheTabletNoticeAndWaitsForRetry()
    {
        var rig = new DocsRig();
        rig.Tablet.UploadFails = new HttpRequestException("The cloud said no.");

        await rig.RunAsync();

        Assert.Equal(1, rig.Delivery.PendingCount);
        Assert.Equal(NoticeKind.ReMarkableDelivery, Assert.Single(rig.Board.Board.Waiting()).Kind);
        rig.Tablet.UploadFails = null;
        Assert.Equal(1, await rig.Delivery.RetryPending());
        Assert.Equal(0, rig.Delivery.PendingCount);
    }

    [Fact]
    public async Task APageThatCannotBeDrawnDoesNotStopTheNext()
    {
        var rig = new DocsRig { Now = Edt(10, 26, 16, 30), Person = Sam };
        rig.Box = Cards().Where(card => card.Id != Id("black-bean-chili")).ToList();

        var done = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.NotBuilt, rig.Sheet(done).Verdict);
        Assert.Equal(MealDocumentVerdict.NotBuilt, rig.RecipeOn(done, 3).Verdict);
        Assert.IsType<ArgumentException>(rig.RecipeOn(done, 3).Failure);
        Assert.Equal(MealDocumentVerdict.Delivered, rig.RecipeOn(done, 1).Verdict);
    }

    [Fact]
    public async Task AnUnchangedRerunDeliversNothing()
    {
        var rig = new DocsRig();
        await rig.RunAsync();
        var uploads = rig.Tablet.Uploads;
        var recorded = rig.Expected.Records;
        var delivered = rig.Delivered.Records;

        var again = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.Unchanged, rig.Sheet(again).Verdict);
        Assert.Equal(uploads, rig.Tablet.Uploads);
        Assert.Equal(recorded, rig.Expected.Records);
        Assert.Equal(delivered, rig.Delivered.Records);
        Assert.DoesNotContain(rig.Tablet.Calls, call => call.StartsWith("trash:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AChangedSheetBeforeTheDeadlineReplacesTheBlankOne()
    {
        var rig = new DocsRig();
        await rig.RunAsync();
        rig.Now = Edt(10, 23, 9);
        rig.Week = MealWeek.Planned(Sunday, Week().Dinners.Select((dinner, index) => index == 4 ? Cooked(4, "turkey-meatballs", Lee) : dinner));

        var again = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.Replaced, rig.Sheet(again).Verdict);
        Assert.Single(rig.Tablet.Titles.Values, rig.SheetTitle);
        Assert.Contains(rig.SheetTitle, rig.Probe.Asked);
        Assert.Equal(2, rig.Expected.Records);
    }

    [Fact]
    public async Task AChangedSheetWithInkOnItIsLeftWhereItIs()
    {
        var rig = new DocsRig();
        await rig.RunAsync();
        rig.Now = Edt(10, 23, 9);
        rig.Probe.Answer = InkProbe.Unknown();
        rig.Week = MealWeek.Planned(Sunday, Week().Dinners.Select((dinner, index) => index == 4 ? Cooked(4, "turkey-meatballs", Lee) : dinner));

        var again = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.Kept, rig.Sheet(again).Verdict);
        Assert.Equal(1, rig.Tablet.Uploads);
        Assert.Equal(1, rig.Expected.Records);
    }

    [Fact]
    public async Task ASheetNotSentByTheDeadlineIsNeverSent()
    {
        var rig = new DocsRig { Now = Edt(10, 25, 9) };

        var done = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.TooLate, rig.Sheet(done).Verdict);
        Assert.Equal(0, rig.Tablet.Uploads);
        Assert.Empty(rig.Expected.Recorded);
    }

    [Fact]
    public async Task AChangedSheetAfterTheDeadlineIsNotReplaced()
    {
        var rig = new DocsRig();
        await rig.RunAsync();
        rig.Now = Edt(10, 25, 9);
        rig.Week = MealWeek.Planned(Sunday, Week().Dinners.Select((dinner, index) => index == 4 ? Cooked(4, "turkey-meatballs", Lee) : dinner));

        var again = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.TooLate, rig.Sheet(again).Verdict);
        Assert.Equal(1, rig.Tablet.Uploads);
    }

    [Fact]
    public async Task APageAlreadyUnderTheTitleIsNotMadeTwice()
    {
        var rig = new DocsRig();
        await rig.Tablet.Upload(rig.SheetTitle, [1, 2, 3]);

        var done = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.Delivered, rig.Sheet(done).Verdict);
        Assert.Single(rig.Tablet.Titles.Values, rig.SheetTitle);
    }

    [Fact]
    public async Task AnUnreadableRosterStopsBeforeAnythingIsTouched()
    {
        var rig = new DocsRig();
        rig.Roster.Answer = new MealRosterRead.Unavailable("The household file cannot be opened.");

        var result = await MealDocumentsStep.RunAsync(rig.Request(), rig.Seams, CancellationToken.None);

        var stopped = Assert.IsType<MealDocumentsResult.Stopped>(result);
        Assert.Equal(MealDocumentsStoppedBecause.RosterUnavailable, stopped.Because);
        Assert.Empty(rig.Tablet.Calls);
        Assert.Empty(rig.Expected.Recorded);
    }

    [Fact]
    public async Task AnUnreadablePantryStopsBeforeAnythingIsTouched()
    {
        var rig = new DocsRig();
        rig.Pantry.Answer = new MealPantryRead.Unreadable("The pantry cannot be read.");

        var result = await MealDocumentsStep.RunAsync(rig.Request(), rig.Seams, CancellationToken.None);

        Assert.Equal(MealDocumentsStoppedBecause.PantryUnreadable, Assert.IsType<MealDocumentsResult.Stopped>(result).Because);
        Assert.Empty(rig.Tablet.Calls);
    }

    [Fact]
    public async Task NoTabletHereIsATypedStop()
    {
        var rig = new DocsRig();

        var result = await MealDocumentsStep.RunAsync(rig.Request() with { DeliverFor = [] }, rig.Seams, CancellationToken.None);

        Assert.Equal(MealDocumentsStoppedBecause.NoTablet, Assert.IsType<MealDocumentsResult.Stopped>(result).Because);
        Assert.Empty(rig.Tablet.Calls);
    }

    [Fact]
    public async Task ANameThatIsNoAdultIsNoTablet()
    {
        var rig = new DocsRig();

        var result = await MealDocumentsStep.RunAsync(rig.Request() with { DeliverFor = ["Robin"] }, rig.Seams, CancellationToken.None);

        Assert.Equal(MealDocumentsStoppedBecause.NoTablet, Assert.IsType<MealDocumentsResult.Stopped>(result).Because);
    }

    [Fact]
    public async Task NoPantryYetMeansAnEmptyOneAndSaysSo()
    {
        var rig = new DocsRig();
        rig.Pantry.Answer = new MealPantryRead.NoneYet("There is no household file on this phone yet.");

        var done = await rig.RunAsync();

        Assert.Equal(MealDocumentVerdict.Delivered, rig.Sheet(done).Verdict);
        Assert.Equal("There is no household file on this phone yet.", done.PantrySentence);
    }

    [Fact]
    public async Task APantryWithUnreadableRecordsIsCounted()
    {
        var rig = new DocsRig();
        rig.Pantry.Answer = new MealPantryRead.Ready(Stocked(), 2);

        var done = await rig.RunAsync();

        Assert.Equal(2, done.UnreadableStaples);
        Assert.Null(done.PantrySentence);
    }

    [Fact]
    public async Task OnlyTheFirstNamedAdultIsServed()
    {
        var rig = new DocsRig();

        var done = await rig.RunAsync(rig.Request() with { DeliverFor = [Lee, Sam] });

        Assert.Equal(Lee, rig.Sheet(done).Person);
        Assert.Equal(Lee, Assert.Single(rig.Expected.Recorded).Person);
        Assert.All(done.Documents.Where(document => document.Verdict != MealDocumentVerdict.NotHere), document => Assert.Equal(Lee, document.Person));
    }

    [Fact]
    public async Task TheStepNeverWritesTheMealAnswers()
    {
        var rig = new DocsRig { Now = Edt(10, 25, 16, 30) };

        await rig.RunAsync();

        Assert.All(rig.Expected.Recorded, item => Assert.True(item.Kind is DeliveryKind.Meals or DeliveryKind.Recipe));
        Assert.Equal(0, rig.Tablet.Calls.Count(call => call.StartsWith("download:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheTrailLinesPassTheFilter()
    {
        var rig = new DocsRig { Now = Edt(10, 25, 16, 30) };
        await rig.RunAsync();

        Assert.NotEmpty(rig.Log.Phrases);
        foreach (var phrase in rig.Log.Phrases)
        {
            Assert.NotNull(TrailLines.Filter("[Oct 21, 12:00 pm] INFO Body — " + phrase));
        }

        Assert.All(rig.Log.Phrases, phrase => Assert.DoesNotContain("Lee", phrase, StringComparison.Ordinal));
    }
}
