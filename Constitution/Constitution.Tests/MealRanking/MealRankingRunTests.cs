using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Infrastructure.Meals;
using Constitution.Tests.MealTaste;
using Constitution.Tests.MealWeekRun;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealRanking;

/// <summary>The weekly run hands the planner a memory built from what happened, and the taste scores. The week is planned on Wednesday 21 October for the week of 25 October.</summary>
public class MealRankingRunTests
{
    private static readonly DateOnly LastWeek = Sunday.AddDays(-7);
    private static readonly DateOnly LastTuesday = LastWeek.AddDays(2);

    private sealed class FakeAnswers(MealAnswersRead reading) : IMealAnswerSource
    {
        public List<DateOnly> Asked { get; } = [];

        public MealAnswersRead Read(DateOnly week)
        {
            Asked.Add(week);
            return reading is MealAnswersRead.Held held
                ? new MealAnswersRead.Held(held.Answers.Where(answer => answer.Week == week).ToList(), held.UnreadableRecords)
                : reading;
        }
    }

    private sealed class FakeAte(AteStoreRead reading) : IAteEstimates
    {
        public AteStoreRead Read() => reading;

        public AteEstimate? Record(AteEstimate estimate) => throw new InvalidOperationException("A run never records an estimate.");

        public void Withdraw(AteEstimate recorded, AteEstimate? previous) => throw new InvalidOperationException("A run never withdraws one.");
    }

    private static MealAnswer TuesdayAnswer(MealVerdict verdict) =>
        new(new PersonId("lee"), LastWeek, MealSheetIds.DinnerYes(DayOfWeek.Tuesday), verdict, verdict == MealVerdict.Denied ? "Not tonight." : null, Edt(10, 19, 20));

    // Last week's Tuesday dinner, lentil curry, was planned; the owner marks it cook this soon. Only the memory of last week decides whether it can be.
    private static MealWeekRig Rig(MealWeekSeamsChange change)
    {
        var rig = new MealWeekRig();
        var week = new StoredMealWeek(
            MealWeek.Planned(LastWeek, Enumerable.Range(0, 7).Select(offset => offset == 2
                ? DinnerChoice.Cooked(LastTuesday, Id("lentil-curry"), "Sam", Edt(10, 20, 17, 15), Edt(10, 20, 18), false, false, "From the box.")
                : DinnerChoice.NothingPlanned(LastWeek.AddDays(offset), "Nothing is planned."))),
            Edt(10, 14, 12),
            [new StoredDish(Id("lentil-curry"), "Invented lentil curry")]);
        rig.Records.Json = MealWeekRecordCodec.Encode(new MealWeekRecord([week]));
        change(rig);
        return rig;
    }

    private delegate void MealWeekSeamsChange(MealWeekRig rig);

    private static async Task<bool> CurryIsCooked(MealWeekRig rig)
    {
        var report = await rig.PlannedAsync(rig.Inputs() with { CookThisSoon = new HashSet<RecipeId> { Id("lentil-curry") } });
        return report.Week.Dinners.Any(dinner => dinner.Recipe == Id("lentil-curry"));
    }

    [Fact]
    public async Task WithoutAnswersLastWeeksPlanStandsIn()
    {
        Assert.False(await CurryIsCooked(Rig(_ => { })));
    }

    [Fact]
    public async Task AnApprovedDinnerStillBlocksTheRepeat()
    {
        var rig = Rig(r => r.SetAnswers(new FakeAnswers(new MealAnswersRead.Held([TuesdayAnswer(MealVerdict.Approved)], 0))));

        Assert.False(await CurryIsCooked(rig));
    }

    [Fact]
    public async Task ADeniedDinnerWasNeverCookedSoItCanComeBack()
    {
        var answers = new FakeAnswers(new MealAnswersRead.Held([TuesdayAnswer(MealVerdict.Denied)], 0));
        var rig = Rig(r => r.SetAnswers(answers));

        Assert.True(await CurryIsCooked(rig));
        Assert.Equal([LastWeek], answers.Asked);
    }

    [Fact]
    public async Task AnAteSomethingElseDinnerWasNeverCookedSoItCanComeBack()
    {
        Assert.True(Constitution.Domain.Meals.AteReading.TryCreate("a bowl of soup", 300, 500, null, null, AteConfidence.Fair, out var reading));
        var rig = Rig(r => r.SetAte(new FakeAte(new AteStoreRead([new AteEstimate(LastTuesday, AteMeal.Dinner, reading!)], false))));

        Assert.True(await CurryIsCooked(rig));
    }

    [Fact]
    public async Task AnUnreadableAnswerFileFallsBackToThePlan()
    {
        var rig = Rig(r => r.SetAnswers(new FakeAnswers(new MealAnswersRead.Unreadable("The household file cannot be read."))));

        Assert.False(await CurryIsCooked(rig));
    }

    [Fact]
    public async Task AnUnreadableEstimateFileFallsBackToThePlan()
    {
        Assert.True(Constitution.Domain.Meals.AteReading.TryCreate("a bowl of soup", 300, 500, null, null, AteConfidence.Fair, out var reading));
        var rig = Rig(r => r.SetAte(new FakeAte(new AteStoreRead([new AteEstimate(LastTuesday, AteMeal.Dinner, reading!)], true))));

        Assert.False(await CurryIsCooked(rig));
    }

    [Fact]
    public async Task WithNoRecordNothingIsAsked()
    {
        var answers = new FakeAnswers(new MealAnswersRead.Held([], 0));
        var rig = new MealWeekRig();
        rig.SetAnswers(answers);

        await rig.PlannedAsync();

        Assert.Empty(answers.Asked);
    }

    [Fact]
    public async Task TheHistoryFileIsStillNeverWritten()
    {
        var rig = Rig(r => r.SetAnswers(new FakeAnswers(new MealAnswersRead.Held([TuesdayAnswer(MealVerdict.Approved)], 0))));

        await rig.PlannedAsync();

        Assert.Equal(0, rig.History.Appends);
    }

    [Fact]
    public async Task ALovedDishIsTheStretchDishWhenNothingElseDecides()
    {
        var plain = new MealWeekRig();
        var plainStretch = (await plain.PlannedAsync()).Week.Dinners.Single(dinner => dinner.IsStretch).Recipe;
        var loved = TasteData.Risotto == plainStretch ? TasteData.Curry : TasteData.Risotto;

        var rig = new MealWeekRig();
        rig.Taste.Answer = new MealTasteRead.Held(
            TasteData.Build([TasteData.Rate(TasteData.Sam, loved, TasteVerdict.Loved), TasteData.Rate(TasteData.Lee, loved, TasteVerdict.Loved)]),
            TasteData.People());
        var report = await rig.PlannedAsync();

        Assert.Equal(loved, report.Week.Dinners.Single(dinner => dinner.IsStretch).Recipe);
    }

    [Fact]
    public async Task NoProfileIsTheSameWeekAsAnEmptyOne()
    {
        var none = new MealWeekRig();
        var empty = new MealWeekRig();
        empty.Taste.Answer = new MealTasteRead.Held(TasteProfile.Empty, TasteData.People());

        var first = await none.PlannedAsync();
        var second = await empty.PlannedAsync();

        Assert.Equal(MealWeekCodec.Write(first.Week), MealWeekCodec.Write(second.Week));
    }
}
