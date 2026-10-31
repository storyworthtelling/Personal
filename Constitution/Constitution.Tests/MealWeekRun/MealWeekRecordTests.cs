using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealWeekRun;

/// <summary>The saved weeks: the shape on disk, what keeping a week does to an earlier copy, and what a re-run does to evenings that are over.</summary>
public class MealWeekRecordTests
{
    private static MealWeek WeekOf(DateOnly start, string? mondayDish = "saffron-rice") => MealWeek.Planned(
        start,
        Enumerable.Range(0, 7).Select(offset => offset == 1 && mondayDish is not null
            ? DinnerChoice.Cooked(start.AddDays(1), Id(mondayDish), Sam, Edt(10, 26, 17, 15), Edt(10, 26, 18), false, false, "From the box.")
            : DinnerChoice.NothingPlanned(start.AddDays(offset), "Nothing is planned.")),
        ["No stretch dish this week: every card has been cooked lately."]);

    private static StoredMealWeek Stored(DateOnly start, string? dish = "saffron-rice", int hour = 12) =>
        new(WeekOf(start, dish), Edt(10, 21, hour), dish is null ? [] : [new StoredDish(Id(dish), "Invented " + dish.Replace('-', ' '))]);

    // ---- the codec ----

    [Fact]
    public void AWeekRoundTripsThroughTheCodec()
    {
        var record = new MealWeekRecord([Stored(Sunday)]);

        var read = Assert.IsType<MealWeekRecordRead.Held>(MealWeekRecordCodec.Decode(MealWeekRecordCodec.Encode(record)));

        var week = Assert.Single(read.Record.Weeks);
        Assert.Equal(Edt(10, 21, 12), week.PlannedAt);
        Assert.Equal(MealWeekCodec.Write(record.Weeks[0].Week), MealWeekCodec.Write(week.Week));
        Assert.Equal(record.Weeks[0].Dishes, week.Dishes);
    }

    [Fact]
    public void TheNamesOnDiskAreFrozen()
    {
        var text = MealWeekRecordCodec.Encode(new MealWeekRecord([Stored(Sunday) with { Dishes = [new StoredDish(Id("saffron-rice"), "Invented rice", true, true)] }]));

        foreach (var name in new[] { "\"version\":1", "\"weeks\":[", "\"weekStart\":\"2026-10-25\"", "\"plannedAt\":", "\"week\":{", "\"dishes\":[", "\"recipe\":\"saffron-rice\"", "\"title\":\"Invented rice\"", "\"slowCooker\":true", "\"isNew\":true" })
        {
            Assert.Contains(name, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NothingSavedIsNothingYetNotAnEmptyRecord()
    {
        Assert.IsType<MealWeekRecordRead.NothingYet>(MealWeekRecordCodec.Decode(null));
        Assert.IsType<MealWeekRecordRead.NothingYet>(MealWeekRecordCodec.Decode("  "));
    }

    [Theory]
    [InlineData("this is not json", MealWeekRecordCodec.NotJson)]
    [InlineData("[]", MealWeekRecordCodec.WrongShape)]
    [InlineData("{\"version\":2,\"weeks\":[]}", MealWeekRecordCodec.WrongShape)]
    [InlineData("{\"version\":1}", MealWeekRecordCodec.WrongShape)]
    [InlineData("{\"version\":1,\"weeks\":[{\"weekStart\":\"2026-10-25\",\"plannedAt\":\"2026-10-21T12:00:00-04:00\",\"week\":{\"version\":1,\"weekStart\":\"2026-10-25\",\"dinners\":[]},\"dishes\":[]}]}", MealWeekRecordCodec.WrongShape)]
    public void AnUnreadableRecordSaysSoInFixedWords(string text, string sentence)
    {
        var read = Assert.IsType<MealWeekRecordRead.Unreadable>(MealWeekRecordCodec.Decode(text));

        Assert.Equal(sentence, read.Reason);
    }

    [Fact]
    public void TheContentKeyIgnoresWhenTheWeekWasFirstPlanned()
    {
        Assert.Equal(MealWeekRecordCodec.ContentKey(Stored(Sunday, hour: 9)), MealWeekRecordCodec.ContentKey(Stored(Sunday, hour: 17)));
        Assert.NotEqual(MealWeekRecordCodec.ContentKey(Stored(Sunday)), MealWeekRecordCodec.ContentKey(Stored(Sunday, "lentil-curry")));
    }

    // ---- the record ----

    [Fact]
    public void KeepingAWeekReplacesAnEarlierCopyOfIt()
    {
        var record = new MealWeekRecord([Stored(Sunday)]).With(Stored(Sunday, "lentil-curry"));

        var week = Assert.Single(record.Weeks);
        Assert.Equal(Id("lentil-curry"), week.Week.Dinners[1].Recipe);
    }

    [Fact]
    public void OnlyTheLatestFourWeeksAreKept()
    {
        var record = MealWeekRecord.Empty;
        foreach (var offset in new[] { 3, 0, 5, 1, 2, 4 })
        {
            record = record.With(Stored(Sunday.AddDays(7 * offset)));
        }

        Assert.Equal(4, record.Weeks.Count);
        Assert.Equal(Sunday.AddDays(14), record.Weeks[0].WeekStart);
        Assert.Equal(Sunday.AddDays(35), record.Weeks[^1].WeekStart);
    }

    [Fact]
    public void AWeekThatWasNotPlannedIsNeverKept()
    {
        var stopped = MealWeek.NotPlanned(Sunday, MealStop.BoxTooSmall, "The recipe box has 4 of 10 cards, so no dinners are planned yet.");

        Assert.Throws<ArgumentException>(() => new MealWeekRecord([Stored(Sunday)]).With(new StoredMealWeek(stopped, Edt(10, 21, 12), [])));
    }

    // ---- an evening that is over stays over ----

    [Fact]
    public void ADinnerThatIsOverIsKeptWhateverTheRePlanSays()
    {
        var before = Stored(Sunday);
        var rePlan = Stored(Sunday, "lentil-curry", hour: 20);

        var kept = MealWeekKeeping.Keep(before, rePlan, Sunday.AddDays(2));

        Assert.Equal(Id("saffron-rice"), kept.Week.Dinners[1].Recipe);
        Assert.Equal(before.PlannedAt, kept.PlannedAt);
        Assert.Contains(kept.Dishes, dish => dish.Recipe == Id("saffron-rice"));
    }

    [Fact]
    public void AnEveningNotYetOverTakesTheRePlan()
    {
        var kept = MealWeekKeeping.Keep(Stored(Sunday), Stored(Sunday, "lentil-curry"), Sunday.AddDays(1));

        Assert.Equal(Id("lentil-curry"), kept.Week.Dinners[1].Recipe);
        Assert.DoesNotContain(kept.Dishes, dish => dish.Recipe == Id("saffron-rice"));
    }

    [Fact]
    public void WithNothingSavedTheRePlanStandsAsItIs()
    {
        var fresh = Stored(Sunday, "lentil-curry");

        Assert.Same(fresh, MealWeekKeeping.Keep(null, fresh, Sunday.AddDays(3)));
    }

    // ---- what the planner is shown of earlier weeks ----

    [Fact]
    public void EarlierPlannedDinnersJoinTheCookedHistory()
    {
        var cooked = new[] { new CookedMeal(Id("tomato-pasta"), Sunday.AddDays(-20)) };
        var record = new MealWeekRecord([Stored(Sunday.AddDays(-7))]);

        var merged = PlannedHistory.Merge(cooked, record, Sunday);

        Assert.Equal(2, merged.Count);
        Assert.Contains(new CookedMeal(Id("saffron-rice"), Sunday.AddDays(-6)), merged);
    }

    [Fact]
    public void TheWeekBeingPlannedNeverCountsAgainstItself()
    {
        var record = new MealWeekRecord([Stored(Sunday), Stored(Sunday.AddDays(7), "lentil-curry")]);

        Assert.Empty(PlannedHistory.Merge([], record, Sunday));
    }

    [Fact]
    public void ADinnerBothCookedAndPlannedIsOneEntry()
    {
        var cooked = new[] { new CookedMeal(Id("saffron-rice"), Sunday.AddDays(-6)) };

        var merged = PlannedHistory.Merge(cooked, new MealWeekRecord([Stored(Sunday.AddDays(-7))]), Sunday);

        Assert.Single(merged);
    }

    [Fact]
    public void WithNoRecordTheHistoryIsJustWhatWasCooked()
    {
        var cooked = new[] { new CookedMeal(Id("tomato-pasta"), Sunday.AddDays(-3)) };

        Assert.Equal(cooked, PlannedHistory.Merge(cooked, null, Sunday));
    }

    // ---- which saved week a screen reads ----

    [Fact]
    public void TheWeekThatHoldsTheDayIsTheOneRead()
    {
        var record = new MealWeekRecord([Stored(Sunday), Stored(Sunday.AddDays(7), "lentil-curry")]);

        Assert.Equal(Sunday, MealWeekSnapshots.Pick(record, Sunday.AddDays(3))!.WeekStart);
        Assert.Equal(Sunday.AddDays(7), MealWeekSnapshots.Pick(record, Sunday.AddDays(7))!.WeekStart);
    }

    [Fact]
    public void WithNoWeekInProgressTheNextOneToComeIsRead()
    {
        var record = new MealWeekRecord([Stored(Sunday.AddDays(7))]);

        Assert.Equal(Sunday.AddDays(7), MealWeekSnapshots.Pick(record, Sunday.AddDays(3))!.WeekStart);
    }

    [Fact]
    public void AWeekThatIsOverIsNotAPlan()
    {
        Assert.Null(MealWeekSnapshots.Pick(new MealWeekRecord([Stored(Sunday)]), Sunday.AddDays(7)));
        Assert.Null(MealWeekSnapshots.Pick(MealWeekRecord.Empty, Sunday));
    }

    [Fact]
    public void TheSnapshotNamesTheDishesTheWeekWasPlannedWith()
    {
        var snapshot = MealWeekSnapshots.From(Stored(Sunday));

        Assert.Equal("Invented saffron rice", snapshot.Dishes[Id("saffron-rice")].Title);
        Assert.Null(snapshot.EatingOut);
        Assert.Null(snapshot.ApprovedOn);
    }
}
