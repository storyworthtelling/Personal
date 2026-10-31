using Constitution.Domain.Calendar;
using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Screens;
using Constitution.Domain.Placement;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Voice;
using Constitution.Tests.MealSheet;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;
using MealCalendar = Constitution.Domain.Placement.MealPlacement;

namespace Constitution.Tests.MealPlanning;

/// <summary>
/// The dinners, the groceries, the meal sheet and the words about them follow the household's planning week, which runs Sunday
/// through Saturday (<see cref="PlanningWeek"/>). Each test builds its own Sunday week rather than leaning on a shared fixture,
/// so none of them can pass by the fixture and the code agreeing on the wrong day.
/// </summary>
public class SundayMealWeekTests
{
    private static readonly string[] SundayFirst = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static MealWeekRequest SundayRequest(DateOnly start)
    {
        var rules = Rules();
        var time = rules.Adults.Select(adult => BusyTime.For(adult.Name, start, start.AddDays(6), rules, Calendars())).ToList();
        return new MealWeekRequest(start, Box(), rules, time, null, null, null, null, null, 1, SaturdayNoon);
    }

    private static MealWeek NothingFrom(DateOnly start) =>
        MealWeek.Planned(start, Enumerable.Range(0, 7).Select(offset => DinnerChoice.NothingPlanned(start.AddDays(offset), "Nothing is planned.")));

    // ---- the meal week itself ----

    [Fact]
    public void ThePlannerWeekRunsSundayToSaturday()
    {
        var week = MealPlanner.Plan(SundayRequest(Sunday));

        Assert.Equal(SundayFirst, week.Dinners.Select(dinner => dinner.Day.DayOfWeek.ToString()));
        Assert.Equal(Sunday, week.WeekStart);
    }

    [Fact]
    public void TheStretchDishStillLandsOnThursday()
    {
        var week = MealPlanner.Plan(SundayRequest(Sunday));

        var stretch = Assert.Single(week.Dinners.Where(dinner => dinner.IsStretch));
        Assert.Equal(DayOfWeek.Thursday, stretch.Day.DayOfWeek);
        Assert.Equal(4, week.Dinners.ToList().IndexOf(stretch));
    }

    [Fact]
    public void AMondayStartIsRefusedByTheRequest()
    {
        var error = Assert.Throws<ArgumentException>(() => SundayRequest(Monday));

        Assert.Contains("Sunday", error.Message, StringComparison.Ordinal);
        Assert.Contains("Monday", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMondayStartIsRefusedByAPlannedWeek()
    {
        var error = Assert.Throws<ArgumentException>(() => NothingFrom(Monday));

        Assert.Contains("Sunday", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASundayStartIsAcceptedByAPlannedWeek() =>
        Assert.Equal(Sunday, NothingFrom(Sunday).WeekStart);

    [Fact]
    public void ThePlansRulesAcceptItsOwnSundayWeek()
    {
        var request = SundayRequest(Sunday);

        Assert.Empty(MealRules.Violations(request, MealPlanner.Plan(request)));
    }

    // ---- the groceries ----

    [Fact]
    public void ATripForAMondayWeekIsRefused()
    {
        var rules = Rules();

        var error = Assert.Throws<ArgumentException>(() => GroceryPlacement.Request(Monday, rules, [], Edt(10, 22, 8)));

        Assert.Contains("Sunday", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTripGoesInTheThreeDaysBeforeTheSunday()
    {
        var rules = Rules();
        var people = rules.Adults.Select(adult => BusyTime.For(adult.Name, Sunday.AddDays(-3), Sunday.AddDays(6), rules, Calendars())).ToList();

        var trip = GroceryPlacement.Request(Sunday, rules, people, Edt(10, 20, 8))!;

        Assert.Equal(
            [Edt(10, 24, 9), Edt(10, 23, 9), Edt(10, 22, 9)],
            trip.Windows.Select(window => window.Start).ToList());
        Assert.Equal(
            [DayOfWeek.Saturday, DayOfWeek.Friday, DayOfWeek.Thursday],
            trip.Windows.Select(window => window.Start.DayOfWeek).ToList());
    }

    // ---- the calendar keys ----

    [Fact]
    public void TheEveningsOfASundayWeekKeepTheirWeekdayWords()
    {
        var week = MealPlacement.MealPlacementData.WeekOf(
            MealPlacement.MealPlacementData.CookedOn(0, MealPlacement.MealPlacementData.Chili, Sam, 45),
            MealPlacement.MealPlacementData.CookedOn(1, MealPlacement.MealPlacementData.Pasta, Lee, 30));

        var keys = MealCalendar.Requests(week, Rules(), MealPlacement.MealPlacementData.Cards).Select(request => request.Identity).ToList();

        Assert.Equal(["cook-sun", "cook-mon"], keys);
    }

    [Fact]
    public void ASundayWeekHasTheNumberOfTheMondayWeekItContains()
    {
        var key = MealCalendar.KeyOf(MealEventRole.Cook, Sam, Sunday, Monday);

        Assert.Equal(PlacementKey.ForHousehold(Sam, PlacementKey.WeekIndexOf(Monday), "cook-mon"), key);
        Assert.Equal(PlacementKey.WeekIndexOf(Sunday), PlacementKey.WeekIndexOf(Monday));
    }

    [Fact]
    public void AKeyForAMondayWeekIsRefused()
    {
        var error = Assert.Throws<ArgumentException>(() => MealCalendar.KeyOf(MealEventRole.Cook, Sam, Monday, Monday.AddDays(1)));

        Assert.Contains("Sunday", error.Message, StringComparison.Ordinal);
    }

    // ---- the meal sheet ----

    [Fact]
    public void TheSheetRowsReadSundayToSaturday()
    {
        var model = new MealSheetModel(NothingFrom(Sunday), MealSheetSamples.Dishes(), MealSheetSamples.Deadline, null, null);

        var page = MealSheetDocument.Build(model).Single();
        var tops = SundayFirst.Select(Enum.Parse<DayOfWeek>).Select(day => page.Map.ById(MealSheetIds.DinnerYes(day)).Bounds.Top).ToList();
        var labels = page.Printed.Where(item => item.Name?.EndsWith("-day", StringComparison.Ordinal) == true).OrderBy(item => item.Bounds.Top).Select(item => item.Text).ToList();

        Assert.Equal(tops.Order(), tops);
        Assert.Equal(["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"], labels);
    }

    [Fact]
    public void TheExpectedRulesFollowTheSheetsRows()
    {
        var model = new MealSheetModel(NothingFrom(Sunday), MealSheetSamples.Dishes(), MealSheetSamples.Deadline, null, null);

        var rules = ExpectedDocument.ForMealSheet(model, Sam).Pages.Single().Rules.Cast<MealItemRule>().ToList();

        Assert.Equal(SundayFirst.Select(day => day + "'s dinner: nothing planned"), rules.Select(rule => rule.Subject));
        Assert.Equal(MealSheetIds.DinnerYesSunday, rules[0].YesBoxId);
        Assert.Equal(MealSheetIds.DinnerReasonSaturday, rules[6].ReasonId);
    }

    // ---- the words ----

    [Fact]
    public void DaysAreSpokenInTheOrderOfTheWeek()
    {
        var week = new MealWeekFacts(Sunday, [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Tuesday], [DayOfWeek.Saturday, DayOfWeek.Monday]);

        Assert.Equal("Dinners for the week of Oct 25. Three new recipes, Sunday, Tuesday and Saturday.", SeatFacts.MealSheetIntro(week).Fallback);
        Assert.Contains("Dinner is out on Monday and Saturday.", SeatFacts.MealSheetIntro(week).Facts);
    }

    // ---- the This week view ----

    [Fact]
    public void ThisWeekListsSundayThroughSaturday()
    {
        var snapshot = new MealWeekSnapshot(NothingFrom(Sunday), new Dictionary<Constitution.Domain.Recipes.RecipeId, DishFacts>());
        var tuesday = Sunday.AddDays(2);

        var model = MealsWeekModel.Build(new MealWeekScreenRead.Ready(snapshot), tuesday);

        Assert.Equal(["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"], model.Days.Select(line => line.DayLabel));
        Assert.Equal([true, true, false, false, false, false, false], model.Days.Select(line => line.IsPast));
        Assert.Equal("Tue", Assert.Single(model.Days, line => line.IsToday).DayLabel);
    }
}
