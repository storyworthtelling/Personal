using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Placement;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.MealTaste;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealWeekRun;

/// <summary>
/// The weekly meal run through every seam, on the invented household (Sam and Lee cook; Robin and Juniper eat). The run is planned on
/// Wednesday 21 October 2026 for the week that opens on Sunday 25 October.
/// </summary>
public class MealWeekRunTests
{
    // ---- the lease comes first ----

    [Fact]
    public async Task TheLeaseSaysNoSoNothingIsPlanned()
    {
        var rig = new MealWeekRig { MayPlan = false, LeaseSentence = "The other phone holds this week's planning." };

        var outcome = await rig.RunAsync();

        var stopped = rig.StoppedWith(MealNotPlannedBecause.NotThePlanner, outcome);
        Assert.Equal("The other phone holds this week's planning.", stopped.Sentence);
        Assert.Equal(["lease"], rig.Calls);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task ThePartnerGetsATypedAnswerNotAnError()
    {
        var rig = new MealWeekRig { MayPlan = false };

        var outcome = await rig.RunAsync();

        Assert.IsType<MealWeekOutcome.NotPlanned>(outcome);
        Assert.Equal(0, rig.Calendars.Reads);
        Assert.Empty(rig.Origin.Lines);
    }

    [Fact]
    public async Task TheLeaseIsAskedBeforeAnythingIsRead()
    {
        var rig = new MealWeekRig();

        await rig.PlannedAsync();

        Assert.Equal("lease", rig.Calls[0]);
        Assert.Equal(["lease", "recipes", "history", "records", "taste", "calendars"], rig.Calls);
    }

    // ---- what stops the week ----

    [Fact]
    public async Task ABoxOfNineStopsAtThePlannersOwnMinimum()
    {
        var rig = new MealWeekRig();
        rig.Box.Of(Box().Take(9).ToList());

        var stopped = rig.StoppedWith(MealNotPlannedBecause.BoxTooSmall, await rig.RunAsync());

        Assert.Contains("9 of 10", stopped.Sentence, StringComparison.Ordinal);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task ABoxThatCannotBeReadIsNotAnEmptyOne()
    {
        var rig = new MealWeekRig();
        rig.Box.Answer = new Constitution.Domain.Recipes.Screens.RecipeBoxRead.Unavailable("The household file cannot be read.");

        var stopped = rig.StoppedWith(MealNotPlannedBecause.RecipesUnavailable, await rig.RunAsync());

        Assert.Equal("The household file cannot be read.", stopped.Sentence);
        Assert.Equal(0, rig.Calendars.Reads);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task AnUnreadableHistoryPlansNothing()
    {
        var rig = new MealWeekRig();
        rig.History.Json = "this is not a history";

        var stopped = rig.StoppedWith(MealNotPlannedBecause.HistoryUnreadable, await rig.RunAsync());

        Assert.Equal(MealHistoryCodec.Unusable, stopped.Sentence);
        Assert.Equal(0, rig.Calendars.Reads);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task AnUnreadableTasteProfilePlansNothing()
    {
        var rig = new MealWeekRig();
        rig.Taste.Answer = new MealTasteRead.Unreadable("The saved taste profile is not valid JSON.");

        rig.StoppedWith(MealNotPlannedBecause.TasteUnreadable, await rig.RunAsync());

        Assert.Equal(0, rig.Calendars.Reads);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task AnUnreadableCalendarFailsClosed()
    {
        var rig = new MealWeekRig();
        rig.Calendars.Snapshot = CalendarsWith(lee: CalendarRead.Unreadable("Lee has not shared a calendar yet."));

        var stopped = rig.StoppedWith(MealNotPlannedBecause.CalendarsUnreadable, await rig.RunAsync());

        Assert.Equal("A calendar could not be read for the whole week, so no dinners are planned.", stopped.Sentence);
        rig.AssertNothingWritten();
        Assert.Empty(rig.Writer.All);
    }

    [Fact]
    public async Task AHouseholdWithNoAdultPlansNothing()
    {
        var rig = new MealWeekRig();

        var outcome = await rig.RunAsync(rig.Inputs() with { Rules = Rules() with { Adults = [] } });

        rig.StoppedWith(MealNotPlannedBecause.NoOneToPlan, outcome);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task AWeekThatDoesNotOpenOnASundayIsRefused()
    {
        var rig = new MealWeekRig();

        await Assert.ThrowsAsync<ArgumentException>(() => rig.RunAsync(rig.Inputs(Monday)));
        Assert.Empty(rig.Calls);
    }

    // ---- a planned week ----

    [Fact]
    public async Task AReadableWeekIsPlannedAndItsCookBlocksPlaced()
    {
        var rig = new MealWeekRig();

        var report = await rig.PlannedAsync();

        Assert.True(report.Week.IsPlanned);
        var cooked = report.Week.Dinners.Count(dinner => dinner.Kind == DinnerKind.Cooked);
        Assert.True(cooked >= 5, "An open week should cook most evenings, and cooked " + cooked + ".");
        var blocks = rig.Writer.All.Where(item => item.Title.StartsWith("Cook: ", StringComparison.Ordinal)).ToList();
        Assert.Equal(cooked, blocks.Count);
        Assert.All(blocks, block => Assert.Equal(18, block.End.Hour));
        Assert.All(blocks, block => Assert.Contains(":household:", block.Key.Value, StringComparison.Ordinal));
        Assert.Equal(PlanOriginRulesOnly, report.Origin);
    }

    private const Constitution.Domain.Workouts.PlanOrigin PlanOriginRulesOnly = Constitution.Domain.Workouts.PlanOrigin.RulesOnly;

    [Fact]
    public async Task TheGroceriesTripIsPlacedBeforeTheWeekOpens()
    {
        var rig = new MealWeekRig();

        var report = await rig.PlannedAsync();

        Assert.True(report.GroceryTripAsked);
        var trip = Assert.Single(rig.Writer.All, item => item.Title == "Groceries");
        Assert.True(trip.End <= Edt(10, 25, 0), "The trip is over before the Sunday opens.");
        Assert.True(trip.Start >= rig.Now, "The trip is never before the approval.");
        Assert.Equal(TimeSpan.FromHours(1), trip.End - trip.Start);
    }

    [Fact]
    public async Task ARunOnTheSaturdayLeavesNoDaytimeForATrip()
    {
        var rig = new MealWeekRig { Now = Edt(10, 24, 18) };

        var report = await rig.PlannedAsync();

        Assert.False(report.GroceryTripAsked);
        Assert.DoesNotContain(rig.Writer.All, item => item.Title == "Groceries");
    }

    [Fact]
    public async Task TheCalendarIsReadForTheThreeDaysBeforeToo()
    {
        var rig = new MealWeekRig();

        await rig.PlannedAsync();

        Assert.Equal(Edt(10, 22, 0), rig.Calendars.From);
        Assert.Equal(Edt(11, 1, 0), rig.Calendars.To);
    }

    [Fact]
    public async Task ASamePlanAnyDayBeforeTheWeekComesOutTheSame()
    {
        var first = new MealWeekRig { Now = Edt(10, 21, 12) };
        var later = new MealWeekRig { Now = Edt(10, 23, 8) };

        await first.PlannedAsync();
        await later.PlannedAsync();

        Assert.Equal(
            MealWeekCodec.Write(first.Records.Held().Weeks.Single().Week),
            MealWeekCodec.Write(later.Records.Held().Weeks.Single().Week));
    }

    [Fact]
    public async Task ANeverAgainFromTheTasteProfileIsHonoured()
    {
        // Cook this soon would put the tacos on the first evening that fits; Sam's never again is the only thing that keeps them off.
        var rig = new MealWeekRig();
        var profile = TasteData.Build([TasteData.Rate(TasteData.Sam, TasteData.Tacos, TasteVerdict.NeverAgain)]);
        rig.Taste.Answer = new MealTasteRead.Held(profile, TasteData.People());

        var report = await rig.PlannedAsync(rig.Inputs() with { CookThisSoon = new HashSet<Constitution.Domain.Recipes.RecipeId> { TasteData.Tacos } });

        Assert.DoesNotContain(report.Week.Dinners, dinner => dinner.Recipe == TasteData.Tacos);
    }

    [Fact]
    public async Task TheChildrenHomeEveningsReachThePlanner()
    {
        var rig = new MealWeekRig();
        var inputs = rig.Inputs() with { ChildrenHome = AllEvenings() };

        var report = await rig.PlannedAsync(inputs);

        var byId = Box().ToDictionary(card => card.Id);
        Assert.All(report.Week.Dinners.Where(dinner => dinner.Kind == DinnerKind.Cooked), dinner =>
            Assert.True(byId[dinner.Recipe!.Value].ForKids || dinner.ChildrenEatDifferently));
    }

    // ---- idempotence, and what is kept ----

    [Fact]
    public async Task AnUnchangedRerunWritesNothingNew()
    {
        var rig = new MealWeekRig();
        await rig.PlannedAsync();
        var calendar = rig.Writer.Snapshot();
        var creates = rig.Writer.CreateCalls;
        var keySaves = rig.Keys.Saves;
        var issuedSaves = rig.Issued.Saves;
        var recordSaves = rig.Records.Saves;
        var recordText = rig.Records.Json;
        rig.Now = Edt(10, 21, 15);

        var report = await rig.PlannedAsync();

        Assert.Equal(MealRecordSave.Unchanged, report.RecordSave);
        Assert.Equal(calendar, rig.Writer.Snapshot());
        Assert.Equal(creates, rig.Writer.CreateCalls);
        Assert.Equal(keySaves, rig.Keys.Saves);
        Assert.Equal(issuedSaves, rig.Issued.Saves);
        Assert.Equal(recordSaves, rig.Records.Saves);
        Assert.Equal(recordText, rig.Records.Json);
        Assert.Empty(report.Placement!.Created);
    }

    [Fact]
    public async Task TheFirstRunSavesTheWeek()
    {
        var rig = new MealWeekRig();

        var report = await rig.PlannedAsync();

        Assert.Equal(MealRecordSave.Saved, report.RecordSave);
        Assert.Equal(1, rig.Records.Saves);
        var held = rig.Records.Held().Weeks.Single();
        Assert.Equal(Sunday, held.WeekStart);
        Assert.Equal(rig.Now, held.PlannedAt);
    }

    [Fact]
    public async Task AStopDoesNotEraseTheLastGoodWeek()
    {
        var rig = new MealWeekRig();
        await rig.PlannedAsync();
        var kept = rig.Records.Json;
        var saves = rig.Records.Saves;
        rig.Calendars.Snapshot = CalendarsWith(sam: CalendarRead.Unreadable("Sam has not shared a calendar yet."));

        rig.StoppedWith(MealNotPlannedBecause.CalendarsUnreadable, await rig.RunAsync());

        Assert.Equal(kept, rig.Records.Json);
        Assert.Equal(saves, rig.Records.Saves);
    }

    [Fact]
    public async Task AReRunLaterInTheWeekKeepsTheEveningsThatPassed()
    {
        var rig = new MealWeekRig();
        await rig.PlannedAsync();
        var original = rig.Records.Held().Weeks.Single().Week.Dinners.ToList();
        rig.Now = Edt(10, 27, 12);

        await rig.PlannedAsync();

        var after = rig.Records.Held().Weeks.Single().Week.Dinners;
        Assert.Equal(original[0], after[0]);
        Assert.Equal(original[1], after[1]);
        Assert.Equal(Edt(10, 21, 12), rig.Records.Held().Weeks.Single().PlannedAt);
    }

    [Fact]
    public async Task LastWeeksPlannedDinnersAreNotServedAgain()
    {
        var rig = new MealWeekRig();
        var lastWeek = Sunday.AddDays(-7);
        await rig.PlannedAsync(rig.Inputs(lastWeek));
        var earlier = rig.Records.Held().Weeks.Single().Week.Dinners.Where(dinner => dinner.Kind == DinnerKind.Cooked).ToList();
        rig.Now = Edt(10, 22, 12);

        var report = await rig.PlannedAsync();

        foreach (var dinner in report.Week.Dinners.Where(dinner => dinner.Kind == DinnerKind.Cooked))
        {
            Assert.DoesNotContain(earlier, old => old.Recipe == dinner.Recipe && Math.Abs(dinner.Day.DayNumber - old.Day.DayNumber) < MealPlanner.RepeatDays);
        }
    }

    [Fact]
    public async Task ThePlannedWeekIsNeverWrittenIntoTheCookedHistory()
    {
        var rig = new MealWeekRig();
        await rig.PlannedAsync();

        Assert.Equal(0, rig.History.Appends);
        Assert.Null(rig.History.Json);
    }

    [Fact]
    public async Task ADinnerReallyCookedLastWeekIsRemembered()
    {
        var rig = new MealWeekRig();
        rig.History.Json = MealHistoryCodec.Write([new CookedMeal(Id("lentil-curry"), Sunday.AddDays(-2))]);

        var report = await rig.PlannedAsync();

        Assert.DoesNotContain(report.Week.Dinners, dinner => dinner.Recipe == Id("lentil-curry") && dinner.Day < Sunday.AddDays(12));
    }

    // ---- failures at the calendar are typed ----

    [Fact]
    public async Task AWriteTheCalendarRefusesIsReportedNotThrown()
    {
        var rig = new MealWeekRig();
        rig.Writer.FailingPeople.Add(Sam);
        rig.Writer.FailingPeople.Add(Lee);

        var report = await rig.PlannedAsync();

        Assert.NotEmpty(report.Placement!.Failed);
        Assert.Empty(report.Placement.Created);
        Assert.Equal(0, rig.Keys.Saves);
        Assert.Equal(MealRecordSave.Saved, report.RecordSave);
    }

    [Fact]
    public async Task ACalendarNotSharedYetIsWaitedForNotGuessed()
    {
        var rig = new MealWeekRig();
        rig.Writer.UnavailablePeople.Add(Lee);

        var report = await rig.PlannedAsync();

        Assert.DoesNotContain(rig.Writer.All, item => item.Person == Lee);
        Assert.All(report.Placement!.Plan!.Outcomes.Where(item => item.Person == Lee), item => Assert.IsType<EventUnplaced>(item));
    }

    [Fact]
    public async Task AnUnreadableRecordOfKeysBlocksPlacementAndSaysSo()
    {
        var rig = new MealWeekRig();
        rig.Issued.Json = "not the record";

        var report = await rig.PlannedAsync();

        Assert.True(report.PlacementBlocked);
        Assert.Empty(rig.Writer.All);
        Assert.Equal(0, rig.Keys.Saves);
        Assert.Equal("not the record", rig.Issued.Json);
    }

    [Fact]
    public async Task ThePastIsNeverWrittenTo()
    {
        // Approved on the Monday, run on the Saturday at noon: the trip's Saturday window opens at nine, which has already gone by.
        var rig = new MealWeekRig { Now = Edt(10, 24, 12) };

        var report = await rig.PlannedAsync(rig.Inputs() with { ApprovedAt = Edt(10, 19, 9) });

        Assert.True(report.GroceryTripAsked);
        Assert.All(rig.Writer.All, item => Assert.True(item.Start >= rig.Now, "An event was written for a time that had already passed."));
    }

    // ---- the saved week reaches the screens ----

    [Fact]
    public async Task TheSavedWeekRendersTheThisWeekModelAndTonightsCard()
    {
        var rig = new MealWeekRig();
        var report = await rig.PlannedAsync();
        var source = new MealWeekRecordSource(rig.Records);

        var read = source.Read(Edt(10, 27, 9));
        var model = MealsWeekModel.Build(read, Sunday.AddDays(2));
        var clipping = DinnerClipping.For(read, Sunday.AddDays(2));

        Assert.Equal(WeekState.Planned, model.State);
        Assert.Equal(7, model.Days.Count);
        var tuesday = report.Week.Dinners[2];
        Assert.Equal(tuesday.Kind, model.Tonight!.Kind);
        Assert.NotNull(clipping);
        if (tuesday.Kind == DinnerKind.Cooked)
        {
            Assert.StartsWith("Invented ", model.Tonight.Dish, StringComparison.Ordinal);
            Assert.Equal(tuesday.Cook + " cooks", clipping!.Line);
        }
    }

    [Fact]
    public async Task EveryNightOfTheSavedWeekNamesAKnownDish()
    {
        var rig = new MealWeekRig();
        await rig.PlannedAsync();

        var read = new MealWeekRecordSource(rig.Records).Read(Edt(10, 26, 9));
        var model = MealsWeekModel.Build(read, Sunday.AddDays(1));

        Assert.DoesNotContain(model.Days, line => line.Dish == MealsWeekModel.MissingDishWords);
    }

    // ---- the trail ----

    [Fact]
    public async Task TheRunLeavesNoLineOfItsOwnOnlyThePlannersOrigin()
    {
        var rig = new MealWeekRig();

        await rig.PlannedAsync();

        var line = Assert.Single(rig.Log.Phrases);
        Assert.StartsWith("meal week planned, ", line, StringComparison.Ordinal);
        Assert.Single(rig.Origin.Lines);
    }

    [Fact]
    public async Task EveryStopLeavesOneValueFreeLine()
    {
        var rig = new MealWeekRig { MayPlan = false };

        await rig.RunAsync();

        Assert.Equal(["meal week not planned, not the planner this week"], rig.Log.Phrases);
    }
}
