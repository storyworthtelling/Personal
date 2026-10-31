using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Documents;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealRanking;

/// <summary>
/// The memory the "no repeat in 14 days" rule reads, built from what happened. An invented fortnight: two planned weeks (11 and 18 October) before the week of
/// 25 October, two adults who answered the sheet, and one "I ate something else".
/// </summary>
public class CookedHistoryTests
{
    private static readonly PersonId SamId = new("sam");
    private static readonly PersonId LeeId = new("lee");
    private static readonly DateOnly WeekA = Sunday.AddDays(-14);
    private static readonly DateOnly WeekB = Sunday.AddDays(-7);

    private static DinnerChoice Cooks(DateOnly day, string dish) =>
        DinnerChoice.Cooked(day, Id(dish), "Sam", Edt(day.Month, day.Day, 17, 15), Edt(day.Month, day.Day, 18), false, false, "From the box.");

    private static StoredMealWeek Planned(DateOnly start, params (int Offset, string Dish)[] cooked) =>
        new(
            MealWeek.Planned(start, Enumerable.Range(0, 7).Select(offset => cooked.Any(item => item.Offset == offset)
                ? Cooks(start.AddDays(offset), cooked.First(item => item.Offset == offset).Dish)
                : DinnerChoice.NothingPlanned(start.AddDays(offset), "Nothing is planned."))),
            Edt(10, 9, 12),
            []);

    private static MealAnswer Said(PersonId person, DateOnly week, int offset, MealVerdict verdict) =>
        new(person, week, MealSheetIds.DinnerYes(week.AddDays(offset).DayOfWeek), verdict, verdict == MealVerdict.Denied ? "Not tonight." : null, Edt(10, 9, 20));

    private static AteEstimate Ate(DateOnly day, AteMeal meal)
    {
        Assert.True(AteReading.TryCreate("a bowl of soup", 300, 500, null, null, AteConfidence.Fair, out var reading));
        return new AteEstimate(day, meal, reading!);
    }

    private static IReadOnlyList<CookedMeal> Meals(MealWeekRecord record, params MealAnswer[] answers) =>
        CookedHistory.Build([], record, answers, [], Sunday).Meals;

    private static CookedMeal Meal(string dish, DateOnly day) => new(Id(dish), day);

    [Fact]
    public void AnApprovedDinnerStands()
    {
        var read = CookedHistory.Build([], new MealWeekRecord([Planned(WeekB, (1, "tomato-pasta"))]), [Said(SamId, WeekB, 1, MealVerdict.Approved)], [], Sunday);

        Assert.Equal([Meal("tomato-pasta", WeekB.AddDays(1))], read.Meals);
        Assert.Equal(1, read.FromAnswers);
        Assert.Equal(0, read.FromPlans);
    }

    [Fact]
    public void OneDenialLeavesTheDinnerOut()
    {
        var read = CookedHistory.Build(
            [],
            new MealWeekRecord([Planned(WeekB, (2, "mushroom-risotto"))]),
            [Said(SamId, WeekB, 2, MealVerdict.Approved), Said(LeeId, WeekB, 2, MealVerdict.Denied)],
            [],
            Sunday);

        Assert.Empty(read.Meals);
        Assert.Equal(1, read.Dropped);
    }

    [Fact]
    public void NoAnswerFallsBackToThePlan()
    {
        var read = CookedHistory.Build([], new MealWeekRecord([Planned(WeekB, (3, "bean-burritos"))]), [], [], Sunday);

        Assert.Equal([Meal("bean-burritos", WeekB.AddDays(3))], read.Meals);
        Assert.Equal(1, read.FromPlans);
        Assert.Equal(0, read.FromAnswers);
    }

    [Fact]
    public void UnchangedIsNoAnswer()
    {
        var read = CookedHistory.Build([], new MealWeekRecord([Planned(WeekB, (3, "bean-burritos"))]), [Said(SamId, WeekB, 3, MealVerdict.Unchanged)], [], Sunday);

        Assert.Equal([Meal("bean-burritos", WeekB.AddDays(3))], read.Meals);
        Assert.Equal(1, read.FromPlans);
    }

    [Fact]
    public void AteSomethingElseTakesTheDinnerOut()
    {
        var day = WeekB.AddDays(5);
        var record = new MealWeekRecord([Planned(WeekB, (5, "dumpling-soup"))]);

        var approved = CookedHistory.Build([], record, [Said(SamId, WeekB, 5, MealVerdict.Approved)], [Ate(day, AteMeal.Dinner)], Sunday);
        var unanswered = CookedHistory.Build([], record, [], [Ate(day, AteMeal.Dinner)], Sunday);

        Assert.Empty(approved.Meals);
        Assert.Empty(unanswered.Meals);
        Assert.Equal(1, approved.Dropped);
        Assert.Equal(1, unanswered.Dropped);
    }

    [Fact]
    public void ALunchOrAnotherDayDoesNotTakeTheDinnerOut()
    {
        var day = WeekB.AddDays(5);
        var record = new MealWeekRecord([Planned(WeekB, (5, "dumpling-soup"))]);

        var read = CookedHistory.Build([], record, [], [Ate(day, AteMeal.Lunch), Ate(day.AddDays(1), AteMeal.Dinner), Ate(day, AteMeal.Breakfast)], Sunday);

        Assert.Equal([Meal("dumpling-soup", day)], read.Meals);
    }

    [Fact]
    public void ConfirmedMealsAlwaysStay()
    {
        var day = WeekB.AddDays(5);
        var confirmed = new[] { Meal("herb-omelette", WeekB.AddDays(-1)), Meal("dumpling-soup", day) };

        var read = CookedHistory.Build(
            confirmed,
            new MealWeekRecord([Planned(WeekB, (5, "dumpling-soup"))]),
            [Said(SamId, WeekB, 5, MealVerdict.Denied)],
            [Ate(day, AteMeal.Dinner)],
            Sunday);

        Assert.Equal(confirmed, read.Meals);
    }

    [Fact]
    public void OnlyEarlierWeeksCount()
    {
        var record = new MealWeekRecord([Planned(WeekB, (1, "tomato-pasta")), Planned(Sunday, (1, "lentil-curry")), Planned(Sunday.AddDays(7), (1, "corn-tacos"))]);

        var read = CookedHistory.Build([], record, [], [], Sunday);

        Assert.Equal([Meal("tomato-pasta", WeekB.AddDays(1))], read.Meals);
    }

    [Fact]
    public void OnlyCookedDinnersCount()
    {
        var week = new StoredMealWeek(
            MealWeek.Planned(WeekB, Enumerable.Range(0, 7).Select(offset => offset switch
            {
                1 => Cooks(WeekB.AddDays(1), "tomato-pasta"),
                2 => DinnerChoice.Leftovers(WeekB.AddDays(2), "Leftovers from the evening before."),
                _ => DinnerChoice.NothingPlanned(WeekB.AddDays(offset), "Nothing is planned."),
            })),
            Edt(10, 9, 12),
            []);

        var read = CookedHistory.Build([], new MealWeekRecord([week]), [], [], Sunday);

        Assert.Equal([Meal("tomato-pasta", WeekB.AddDays(1))], read.Meals);
    }

    [Fact]
    public void AnswersForAnotherWeekOrLineDoNotApply()
    {
        var record = new MealWeekRecord([Planned(WeekA, (2, "lentil-curry")), Planned(WeekB, (2, "mushroom-risotto"))]);

        // Week A's Tuesday was denied; that says nothing of week B's Tuesday, nor of A's Wednesday.
        var read = CookedHistory.Build([], record, [Said(SamId, WeekA, 2, MealVerdict.Denied), Said(SamId, WeekA, 3, MealVerdict.Denied)], [], Sunday);

        Assert.Equal([Meal("mushroom-risotto", WeekB.AddDays(2))], read.Meals);
    }

    [Fact]
    public void NoRecordIsJustWhatWasConfirmed()
    {
        var confirmed = new[] { Meal("herb-omelette", Sunday.AddDays(-3)) };

        var read = CookedHistory.Build(confirmed, null, [Said(SamId, WeekB, 1, MealVerdict.Denied)], [], Sunday);

        Assert.Equal(confirmed, read.Meals);
        Assert.Equal(0, read.FromAnswers + read.FromPlans + read.Dropped);
    }

    [Fact]
    public void TheFortnightAddsUp()
    {
        var record = new MealWeekRecord(
        [
            Planned(WeekA, (1, "saffron-rice"), (3, "lentil-curry"), (5, "corn-tacos")),
            Planned(WeekB, (1, "tomato-pasta"), (2, "mushroom-risotto"), (3, "bean-burritos"), (4, "noodle-stir-fry"), (5, "dumpling-soup")),
        ]);
        var answers = new[]
        {
            Said(SamId, WeekA, 1, MealVerdict.Approved),
            Said(LeeId, WeekA, 5, MealVerdict.Denied),
            Said(SamId, WeekB, 1, MealVerdict.Approved),
            Said(LeeId, WeekB, 1, MealVerdict.Approved),
            Said(SamId, WeekB, 2, MealVerdict.Approved),
            Said(LeeId, WeekB, 2, MealVerdict.Denied),
            Said(SamId, WeekB, 3, MealVerdict.Unchanged),
            Said(SamId, WeekB, 5, MealVerdict.Approved),
        };
        var confirmed = new[] { Meal("herb-omelette", WeekB.AddDays(-1)) };

        var read = CookedHistory.Build(confirmed, record, answers, [Ate(WeekB.AddDays(5), AteMeal.Dinner)], Sunday);

        Assert.Equal(
            [
                Meal("herb-omelette", WeekB.AddDays(-1)),
                Meal("saffron-rice", WeekA.AddDays(1)),
                Meal("lentil-curry", WeekA.AddDays(3)),
                Meal("tomato-pasta", WeekB.AddDays(1)),
                Meal("bean-burritos", WeekB.AddDays(3)),
                Meal("noodle-stir-fry", WeekB.AddDays(4)),
            ],
            read.Meals);
        Assert.Equal(2, read.FromAnswers);
        Assert.Equal(3, read.FromPlans);
        Assert.Equal(3, read.Dropped);
    }

    [Fact]
    public void PlannedHistoryIsTheNoAnswerCase()
    {
        var record = new MealWeekRecord([Planned(WeekA, (1, "saffron-rice")), Planned(WeekB, (1, "tomato-pasta"), (4, "noodle-stir-fry"))]);
        var confirmed = new[] { Meal("noodle-stir-fry", WeekB.AddDays(4)), Meal("herb-omelette", Sunday.AddDays(-2)) };

        Assert.Equal(PlannedHistory.Merge(confirmed, record, Sunday), CookedHistory.Build(confirmed, record, [], [], Sunday).Meals);
    }

    [Fact]
    public void NothingIsEverInvented()
    {
        var dishes = Box().Select(card => card.Id.Value).ToArray();
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var weeks = Enumerable.Range(1, random.Next(1, 4)).Select(back => Planned(
                Sunday.AddDays(-7 * back),
                Enumerable.Range(0, 7).Where(_ => random.Next(2) == 0).Select(offset => (offset, dishes[random.Next(dishes.Length)])).ToArray())).ToList();
            var answers = new List<MealAnswer>();
            var ate = new List<AteEstimate>();
            foreach (var week in weeks)
            {
                foreach (var dinner in week.Week.Dinners.Where(item => item.Kind == DinnerKind.Cooked))
                {
                    var offset = dinner.Day.DayNumber - week.WeekStart.DayNumber;
                    if (random.Next(3) == 0)
                    {
                        answers.Add(Said(SamId, week.WeekStart, offset, (MealVerdict)random.Next(3)));
                    }

                    if (random.Next(3) == 0)
                    {
                        answers.Add(Said(LeeId, week.WeekStart, offset, (MealVerdict)random.Next(3)));
                    }

                    if (random.Next(6) == 0)
                    {
                        ate.Add(Ate(dinner.Day, AteMeal.Dinner));
                    }
                }
            }

            var confirmed = Enumerable.Range(0, random.Next(0, 4)).Select(_ => Meal(dishes[random.Next(dishes.Length)], Sunday.AddDays(-random.Next(1, 30)))).Distinct().ToList();
            var read = CookedHistory.Build(confirmed, new MealWeekRecord(weeks), answers, ate, Sunday);

            // Everything out is a confirmed dinner or a planned cooked one that nobody denied and nobody replaced...
            var expected = weeks
                .SelectMany(week => week.Week.Dinners.Where(item => item.Kind == DinnerKind.Cooked).Select(item => (Week: week.WeekStart, Dinner: item)))
                .Where(pair => !answers.Any(answer => answer.Week == pair.Week && answer.ItemId == MealSheetIds.DinnerYes(pair.Dinner.Day.DayOfWeek) && answer.Verdict == MealVerdict.Denied))
                .Where(pair => !ate.Any(item => item.Day == pair.Dinner.Day && item.Meal == AteMeal.Dinner))
                .Select(pair => new CookedMeal(pair.Dinner.Recipe!.Value, pair.Dinner.Day))
                .Concat(confirmed)
                .ToHashSet();
            Assert.True(expected.SetEquals(read.Meals), "seed " + seed);

            // ...and nothing is in twice.
            Assert.Equal(read.Meals.Count, read.Meals.Distinct().Count());
        }
    }
}
