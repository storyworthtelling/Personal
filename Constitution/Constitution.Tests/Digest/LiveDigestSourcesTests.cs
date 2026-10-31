using Constitution.Domain.Body;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.Documents;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Body;

namespace Constitution.Tests.DigestPages;

/// <summary>Job 1 — the real stores behind the seam: the saved workout week and the skip ledger, Body's local store, and the kept meal weeks.</summary>
public class LiveDigestSourcesTests
{
    private static readonly DateOnly Sunday = DigestFixture.WeekStart;

    private sealed class Reports(WorkoutWeekRecordRead read) : IWorkoutWeekReports
    {
        public WorkoutWeekRecordRead Load() => read;

        public void Save(StoredWorkoutWeek week) => throw new InvalidOperationException("The digest never writes the saved week.");
    }

    private sealed class Ledger(SkipLedgerRead read) : ILedgerStore
    {
        public SkipLedgerRead Load() => read;

        public void Save(SkipLedger ledger) => throw new InvalidOperationException("The digest never writes the ledger.");
    }

    private sealed class Kept(MealWeekRecordRead read) : IKeptWeeks
    {
        public MealWeekRecordRead Load() => read;
    }

    private static StoredSession Session(string person, int offset, string kind, int minutes) =>
        new(person, Sunday.AddDays(offset), new DateTimeOffset(2026, 3, 8 + offset, 17, 0, 0, TimeSpan.Zero), kind, minutes, false, [person], "None", [], null);

    private static StoredWorkoutWeek Week(params StoredSession[] sessions) =>
        new(Sunday, new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero), sessions, [new StoredReason("Sam", Sunday.AddDays(3), "Wednesday's walk moved to Thursday for the forecast.")], [], []);

    // Settled the way the read-back settles: a tick is a done day, a struck-out box is a skip.
    private static SkipLedger LedgerWith(params SessionRecord[] records) =>
        records.Aggregate(SkipLedger.Empty, (ledger, record) => ledger.Settle(
            new SessionMarks(record.Person, record.Day, record.Kind, ["done"]),
            [record.Outcome == SessionOutcome.Done ? new InkResolution.Ticked("done") : new InkResolution.Skipped("done")]));

    private static LiveDigestSources Make(
        WorkoutWeekRecordRead? week = null, SkipLedgerRead? ledger = null, IBodyStore? body = null, MealWeekRecordRead? meals = null) =>
        new(
            new Reports(week ?? new WorkoutWeekRecordRead.NothingYet()),
            new Ledger(ledger ?? new SkipLedgerRead(SkipLedger.Empty, 0, false)),
            body ?? new MemoryBodyStore(),
            new Kept(meals ?? new MealWeekRecordRead.NothingYet()));

    [Fact]
    public void WorkoutsJoinThePlanToHowEachDayEnded()
    {
        var sources = Make(
            new WorkoutWeekRecordRead.Held(Week(Session("Sam", 1, "Strength", 30), Session("Sam", 3, "Mobility", 20), Session("Lee", 1, "Walk", 40))),
            new SkipLedgerRead(LedgerWith(
                new SessionRecord("Sam", Sunday.AddDays(1), SessionKind.Strength, SessionOutcome.Done),
                new SessionRecord("Sam", Sunday.AddDays(3), SessionKind.Mobility, SessionOutcome.Skipped)), 0, false));
        var held = Assert.IsType<DigestRead<DigestWorkoutWeek>.Held>(sources.Workouts("sam", Sunday));
        Assert.Equal(2, held.Value.Sessions.Count);
        Assert.Equal(SessionOutcome.Done, held.Value.Sessions[0].Outcome);
        Assert.Equal(30, held.Value.Sessions[0].PlannedMinutes);
        Assert.Equal(SessionOutcome.Skipped, held.Value.Sessions[1].Outcome);
        Assert.Equal(["Wednesday's walk moved to Thursday for the forecast."], held.Value.Moved);
    }

    [Fact]
    public void ASessionOutsideTheWeekIsNotCounted()
    {
        var sources = Make(new WorkoutWeekRecordRead.Held(Week(Session("Sam", 1, "Strength", 30), Session("Sam", 9, "Walk", 30))));
        var held = Assert.IsType<DigestRead<DigestWorkoutWeek>.Held>(sources.Workouts("Sam", Sunday));
        Assert.Equal(Sunday.AddDays(1), Assert.Single(held.Value.Sessions).Day);
    }

    [Fact]
    public void ADayNobodyReadIsUnsettled()
    {
        var sources = Make(new WorkoutWeekRecordRead.Held(Week(Session("Sam", 1, "Strength", 30))));
        var held = Assert.IsType<DigestRead<DigestWorkoutWeek>.Held>(sources.Workouts("Sam", Sunday));
        Assert.Equal(SessionOutcome.Unsettled, Assert.Single(held.Value.Sessions).Outcome);
    }

    [Fact]
    public void NoSavedWeekIsAnEmptyWeekNotAFailure()
    {
        var held = Assert.IsType<DigestRead<DigestWorkoutWeek>.Held>(Make().Workouts("Sam", Sunday));
        Assert.Empty(held.Value.Sessions);
    }

    [Fact]
    public void AnotherWeeksSavedPlanIsNotThisWeeks()
    {
        var other = Week(Session("Sam", 1, "Strength", 30)) with { WeekStart = Sunday.AddDays(-7) };
        var held = Assert.IsType<DigestRead<DigestWorkoutWeek>.Held>(Make(new WorkoutWeekRecordRead.Held(other)).Workouts("Sam", Sunday));
        Assert.Empty(held.Value.Sessions);
    }

    [Fact]
    public void AnUnreadableWeekOrLedgerStopsIt()
    {
        var week = Assert.IsType<DigestRead<DigestWorkoutWeek>.Unavailable>(Make(new WorkoutWeekRecordRead.Unreadable("The saved week is damaged.")).Workouts("Sam", Sunday));
        Assert.Equal("The saved week is damaged.", week.Reason);
        var ledger = Assert.IsType<DigestRead<DigestWorkoutWeek>.Unavailable>(
            Make(new WorkoutWeekRecordRead.Held(Week()), new SkipLedgerRead(SkipLedger.Empty, 0, true)).Workouts("Sam", Sunday));
        Assert.Contains("ledger", ledger.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BodyIsTheLocalStoresReadingsAndNothingElse()
    {
        var store = new MemoryBodyStore();
        store.Upsert([new BodyReading(BodySignal.Weight, DigestFixture.Saturday.AddDays(-1), 180.0)]);
        var held = Assert.IsType<DigestRead<IReadOnlyList<BodyReading>>.Held>(Make(body: store).Readings("Sam"));
        Assert.Single(held.Value);
    }

    private static StoredMealWeek MealWeek(DateOnly start, bool withStretch)
    {
        var dinners = new List<DinnerChoice>();
        var dishes = new List<StoredDish>();
        for (var i = 0; i < 7; i++)
        {
            var day = start.AddDays(i);
            if (i < 5)
            {
                var id = new RecipeId("dish-" + i);
                dinners.Add(DinnerChoice.Cooked(day, id, "Sam", new DateTimeOffset(day, new TimeOnly(17, 0), TimeSpan.Zero), new DateTimeOffset(day, new TimeOnly(18, 0), TimeSpan.Zero), withStretch && i == 2, false, "It fits."));
                dishes.Add(new StoredDish(id, "Dish " + i, false, i == 4));
            }
            else
            {
                dinners.Add(DinnerChoice.EatOut(day, "A night out."));
            }
        }

        return new StoredMealWeek(Constitution.Domain.Meals.MealWeek.Planned(start, dinners), new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero), dishes);
    }

    [Fact]
    public void MealsAreThreeFactsFromTheKeptWeek()
    {
        var record = new MealWeekRecord([MealWeek(Sunday, true), MealWeek(Sunday.AddDays(7), false)]);
        var held = Assert.IsType<DigestRead<DigestMealsWeek>.Held>(Make(meals: new MealWeekRecordRead.Held(record)).Meals(Sunday));
        Assert.Equal(3, held.Value.Facts.Count);
        Assert.Contains("Five dinners at home", held.Value.Facts[0]);
        Assert.Contains("Two nights out", held.Value.Facts[1]);
        Assert.Contains("One new recipe", held.Value.Facts[2]);
        Assert.Equal("Dish 2", held.Value.StretchDish);
        Assert.Null(held.Value.LovedDish);
    }

    [Fact]
    public void NextWeekListsTheKeptDinnersByTitle()
    {
        var record = new MealWeekRecord([MealWeek(Sunday, false), MealWeek(Sunday.AddDays(7), false)]);
        var held = Assert.IsType<DigestRead<DigestNextWeek>.Held>(Make(meals: new MealWeekRecordRead.Held(record)).Next("Sam", Sunday.AddDays(7)));
        Assert.Equal(5, held.Value.Dinners.Count);
        Assert.Equal("Dish 0", held.Value.Dinners[0].Text);
        Assert.Equal(Sunday.AddDays(7), held.Value.Dinners[0].Day);
        Assert.Empty(held.Value.Workouts);
    }

    [Fact]
    public void NoKeptWeekIsAnEmptyMealsPageNotAFailure()
    {
        var sources = Make();
        var meals = Assert.IsType<DigestRead<DigestMealsWeek>.Held>(sources.Meals(Sunday));
        Assert.Empty(meals.Value.Facts);
        Assert.Empty(Assert.IsType<DigestRead<DigestNextWeek>.Held>(sources.Next("Sam", Sunday.AddDays(7))).Value.Dinners);
    }

    [Fact]
    public void AnUnreadableMealRecordStopsIt()
    {
        var stopped = Assert.IsType<DigestRead<DigestMealsWeek>.Unavailable>(Make(meals: new MealWeekRecordRead.Unreadable("The saved meal weeks could not be opened.")).Meals(Sunday));
        Assert.Equal("The saved meal weeks could not be opened.", stopped.Reason);
    }
}
