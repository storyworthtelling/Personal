using Constitution.Domain.Voice;

namespace Constitution.Tests.Voice;

/// <summary>
/// C-9.2.2 — what each of the six seats is told, and the plain sentence it shows when there is no
/// voice. The fallback rows are book page 18's, reproduced character for character from invented
/// facts: they are built from the same facts the voiced line is given, so the plain page and the
/// voiced page can never describe two different days.
///
/// <para>The bugs these reintroduce: a fallback worded slightly differently from the book; a Body line
/// that softens a worsening (so a rising weight reads as "holding steady"); a signal set against a
/// norm instead of the owner's own past; a seat handed more than its own facts.</para>
/// </summary>
public class SeatFactsTests
{
    // ---- page 18, row by row ----

    private static TodayFacts SlowStart() => new(new TimeOnly(5, 30), "strength", 40, DinnerPlan.At("lentil soup"));

    private static RestDayFacts RestDay() => new(DinnerPlan.Out);

    /// <summary>Sleep slipped; everything else is within its own band of last month.</summary>
    private static BodyMonthFacts SleepSlipped(string? skippedDevice = null) => new(
        [
            new SignalMonth("Sleep", SignalUnit.Hours, 6.2, 6.87, RisingIsWorse: false),
            new SignalMonth("Resting heart rate", SignalUnit.BeatsPerMinute, 58, 59, RisingIsWorse: true),
            new SignalMonth("Steps", SignalUnit.Steps, 8000, 8100, RisingIsWorse: false),
        ],
        skippedDevice);

    private static SignalFacts SleepSignal() =>
        new("Sleep", SignalUnit.Hours, 6.2, "last night", 6.87, "October", RisingIsWorse: false);

    private static ProgressionFacts SquatUp() => new("goblet squat", 25, 20);

    private static MealWeekFacts NovemberWeek() =>
        new(new DateOnly(2026, 11, 15), [DayOfWeek.Thursday], [DayOfWeek.Friday]);

    [Fact]
    public void FallbacksMatchPageEighteen()
    {
        Assert.Equal("Strength at 5:30. Lentil soup tonight.", SeatFacts.Today(SlowStart()).Fallback);
        Assert.Equal("A rest day. Dinner is out tonight.", SeatFacts.RestDay(RestDay()).Fallback);
        Assert.Equal("Sleep is down this month. The other signals are close to last month.",
            SeatFacts.BodyMonthly(SleepSlipped()).Fallback);
        Assert.Equal("No watch readings yesterday.", SeatFacts.BodyMonthly(SleepSlipped("watch")).Fallback);
        Assert.Equal("Sleep: 6.2 hours last night, 40 minutes less than October's average.",
            SeatFacts.Signal(SleepSignal()).Fallback);
        Assert.Equal("25 lb today, up from 20.", SeatFacts.WorkoutEncouragement(SquatUp())!.Fallback);
        Assert.Equal("Dinners for the week of Nov 15. One new recipe, Thursday.",
            SeatFacts.MealSheetIntro(NovemberWeek()).Fallback);
    }

    [Fact]
    public void TheFallbackFunctionAndTheRequestAgree()
    {
        // The request carries the fallback SeatFallbacks builds from the same facts: one source.
        Assert.Equal(SeatFallbacks.For(SlowStart()), SeatFacts.Today(SlowStart()).Fallback);
        Assert.Equal(SeatFallbacks.For(RestDay()), SeatFacts.RestDay(RestDay()).Fallback);
        Assert.Equal(SeatFallbacks.For(SleepSlipped()), SeatFacts.BodyMonthly(SleepSlipped()).Fallback);
        Assert.Equal(SeatFallbacks.For(SleepSignal()), SeatFacts.Signal(SleepSignal()).Fallback);
        Assert.Equal(SeatFallbacks.For(SquatUp()), SeatFacts.WorkoutEncouragement(SquatUp())!.Fallback);
        Assert.Equal(SeatFallbacks.For(NovemberWeek()), SeatFacts.MealSheetIntro(NovemberWeek()).Fallback);
    }

    [Fact]
    public void EachBuilderNamesItsOwnSeat()
    {
        Assert.Equal(VoiceSeat.TodayTopLine, SeatFacts.Today(SlowStart()).Seat);
        Assert.Equal(VoiceSeat.RestDayLine, SeatFacts.RestDay(RestDay()).Seat);
        Assert.Equal(VoiceSeat.BodyMonthly, SeatFacts.BodyMonthly(SleepSlipped()).Seat);
        Assert.Equal(VoiceSeat.BodyMonthly, SeatFacts.BodyMonthly(SleepSlipped("watch")).Seat);
        Assert.Equal(VoiceSeat.SignalSentence, SeatFacts.Signal(SleepSignal()).Seat);
        Assert.Equal(VoiceSeat.WorkoutEncouragement, SeatFacts.WorkoutEncouragement(SquatUp())!.Seat);
        Assert.Equal(VoiceSeat.MealSheetIntro, SeatFacts.MealSheetIntro(NovemberWeek()).Seat);
    }

    // ---- the fallbacks, beyond the one example per row ----

    [Fact]
    public void TodayWithADinnerOutSaysSo()
    {
        var request = SeatFacts.Today(new TodayFacts(new TimeOnly(17, 30), "intervals", 30, DinnerPlan.Out));
        Assert.Equal("Intervals at 5:30. Dinner is out tonight.", request.Fallback);
    }

    [Theory]
    [InlineData(0, 5, "12:05")]
    [InlineData(12, 0, "12:00")]
    [InlineData(13, 45, "1:45")]
    [InlineData(23, 59, "11:59")]
    public void TodayTellsTimeOnTheTwelveHourClock(int hour, int minute, string expected)
    {
        var request = SeatFacts.Today(new TodayFacts(new TimeOnly(hour, minute), "strength", 40, DinnerPlan.At("lentil soup")));
        Assert.StartsWith($"Strength at {expected}.", request.Fallback, StringComparison.Ordinal);
    }

    [Fact]
    public void ARestDayWithADinnerAtHomeNamesTheDish()
    {
        Assert.Equal("A rest day. Roast chicken tonight.",
            SeatFacts.RestDay(new RestDayFacts(DinnerPlan.At("roast chicken"))).Fallback);
    }

    public static IEnumerable<object[]> NewRecipeWeeks()
    {
        yield return [Array.Empty<DayOfWeek>(), "No new recipes this week."];
        yield return [new[] { DayOfWeek.Thursday }, "One new recipe, Thursday."];
        yield return [new[] { DayOfWeek.Thursday, DayOfWeek.Tuesday }, "Two new recipes, Tuesday and Thursday."];
        yield return [new[] { DayOfWeek.Saturday, DayOfWeek.Tuesday, DayOfWeek.Thursday }, "Three new recipes, Tuesday, Thursday and Saturday."];
        yield return [new[] { DayOfWeek.Monday, DayOfWeek.Sunday }, "Two new recipes, Sunday and Monday."];
        yield return [new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }, "Two new recipes, Sunday and Saturday."];
    }

    [Theory]
    [MemberData(nameof(NewRecipeWeeks))]
    public void TheMealIntroCountsTheNewRecipesAndNamesTheirDaysInWeekOrder(DayOfWeek[] days, string expected)
    {
        var week = new MealWeekFacts(new DateOnly(2026, 11, 15), days, []);

        Assert.Equal($"Dinners for the week of Nov 15. {expected}", SeatFacts.MealSheetIntro(week).Fallback);
    }

    [Fact]
    public void AnUnchangedLoadIsNotNewsAndAFallingOneIsNotCheered()
    {
        // Encouragement is progression news. No news is no request: nothing is asked of the voice, which
        // is different from a voiced line that is hidden.
        Assert.Null(SeatFacts.WorkoutEncouragement(new ProgressionFacts("goblet squat", 20, 20)));
        Assert.Null(SeatFacts.WorkoutEncouragement(new ProgressionFacts("goblet squat", 15, 20)));
    }

    [Fact]
    public void ALoadWithAHalfPoundKeepsIt()
    {
        Assert.Equal("27.5 lb today, up from 25.",
            SeatFacts.WorkoutEncouragement(new ProgressionFacts("press", 27.5, 25))!.Fallback);
    }

    [Fact]
    public void ASignalWithNoChangeSaysSo()
    {
        var facts = new SignalFacts("Sleep", SignalUnit.Hours, 6.87, "last night", 6.87, "October", RisingIsWorse: false);
        Assert.Equal("Sleep: 6.9 hours last night, about the same as October's average.",
            SeatFacts.Signal(facts).Fallback);
    }

    [Fact]
    public void ASignalAboveItsOwnPastSaysMore()
    {
        var facts = new SignalFacts("Steps", SignalUnit.Steps, 9200, "yesterday", 8000, "October", RisingIsWorse: false);
        Assert.Equal("Steps: 9,200 steps yesterday, 1,200 steps more than October's average.",
            SeatFacts.Signal(facts).Fallback);
    }

    // ---- what each seat is told ----

    [Fact]
    public void TodayIsToldTheWorkoutAndTheDinnerAndNothingElse()
    {
        var facts = SeatFacts.Today(SlowStart()).Facts;

        Assert.Equal(
            ["Workout today: strength at 5:30 AM, about 40 minutes.", "Dinner tonight: lentil soup."],
            facts);
    }

    [Fact]
    public void TodayWithADinnerOutIsToldNobodyIsCooking()
    {
        var facts = SeatFacts.Today(new TodayFacts(new TimeOnly(17, 30), "intervals", 30, DinnerPlan.Out)).Facts;
        Assert.Equal(
            ["Workout today: intervals at 5:30 PM, about 30 minutes.", "Dinner tonight is out; nobody is cooking at home."],
            facts);
    }

    [Fact]
    public void ARestDayIsToldItIsOneAndTheDinner()
    {
        Assert.Equal(["Today is a rest day, with no workout.", "Dinner tonight is out; nobody is cooking at home."],
            SeatFacts.RestDay(RestDay()).Facts);
    }

    [Fact]
    public void BodyIsToldEachMoverThenTheSteadyOnesAsOneFact()
    {
        var facts = SeatFacts.BodyMonthly(SleepSlipped()).Facts;

        Assert.Equal(
        [
            "Sleep is down this month, which is worse: 6.2 hours on average, against 6.9 hours last month.",
            "Close to last month: Resting heart rate, Steps.",
        ], facts);
    }

    [Fact]
    public void TheDeviceReminderReplacesTheMonthThatDay()
    {
        var facts = SeatFacts.BodyMonthly(SleepSlipped("watch")).Facts;

        Assert.Equal(["No watch readings yesterday.", "Today's line has a gap because of that."], facts);
    }

    [Fact]
    public void ASignalIsToldItsLatestReadingItsOwnPastAndTheGap()
    {
        var facts = SeatFacts.Signal(SleepSignal()).Facts;

        Assert.Equal(
        [
            "Sleep: 6.2 hours last night.",
            "Their own October average: 6.9 hours.",
            "That is 40 minutes less than their own October average, which is worse.",
            "The comparison is with their own past only.",
        ], facts);
    }

    [Fact]
    public void EncouragementIsToldTheProgressionAndNothingElse()
    {
        Assert.Equal(
        [
            "Exercise: goblet squat.",
            "Load today: 25 lb, up from 20 lb last time.",
            "That is 5 lb more.",
        ], SeatFacts.WorkoutEncouragement(SquatUp())!.Facts);
    }

    [Fact]
    public void TheMealIntroIsToldTheWeekTheNewRecipesAndTheNightsOut()
    {
        Assert.Equal(
        [
            "The dinner sheet is for the week of Nov 15.",
            "One new recipe, on Thursday.",
            "Dinner is out on Friday.",
        ], SeatFacts.MealSheetIntro(NovemberWeek()).Facts);
    }

    [Fact]
    public void AWeekWithNoNightsOutSaysNothingOfThem()
    {
        var facts = SeatFacts.MealSheetIntro(new MealWeekFacts(new DateOnly(2026, 11, 15), [DayOfWeek.Thursday], [])).Facts;
        Assert.DoesNotContain(facts, fact => fact.Contains("out", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Body states worsening plainly ----

    [Fact]
    public void BodyStatesWorseningPlainly()
    {
        var request = SeatFacts.BodyMonthly(SleepSlipped());

        Assert.Contains("worse", request.Facts[0], StringComparison.Ordinal);
        Assert.StartsWith("Sleep is down this month.", request.Fallback, StringComparison.Ordinal);
    }

    [Fact]
    public void ARisingWeightIsWorsening()
    {
        // D70: a rising weight counts as worsening, so it is stated as such, not as movement.
        var weight = new SignalMonth("Weight", SignalUnit.Pounds, 184, 181, RisingIsWorse: true);
        var request = SeatFacts.BodyMonthly(new BodyMonthFacts([weight]));

        Assert.Equal("Weight is up this month, which is worse: 184 lb on average, against 181 lb last month.", request.Facts[0]);
        Assert.Equal("Weight is up this month.", request.Fallback);
    }

    [Fact]
    public void AFallingWeightIsBetterNotWorse()
    {
        var weight = new SignalMonth("Weight", SignalUnit.Pounds, 178, 181, RisingIsWorse: true);
        var request = SeatFacts.BodyMonthly(new BodyMonthFacts([weight]));

        Assert.Contains("which is better", request.Facts[0], StringComparison.Ordinal);
        Assert.DoesNotContain("worse", request.Facts[0], StringComparison.Ordinal);
    }

    [Fact]
    public void WorseningComesFirstWhateverOrderTheyArriveIn()
    {
        var request = SeatFacts.BodyMonthly(new BodyMonthFacts(
        [
            new SignalMonth("Steps", SignalUnit.Steps, 10000, 8000, RisingIsWorse: false, NamePlural: true),
            new SignalMonth("Weight", SignalUnit.Pounds, 184, 181, RisingIsWorse: true),
            new SignalMonth("Resting heart rate", SignalUnit.BeatsPerMinute, 58, 58, RisingIsWorse: true),
        ]));

        Assert.StartsWith("Weight is up", request.Facts[0], StringComparison.Ordinal);
        Assert.StartsWith("Steps are up", request.Facts[1], StringComparison.Ordinal);
        Assert.Equal("Weight is up this month. Steps are up this month. The other signals are close to last month.", request.Fallback);
    }

    [Theory]
    [InlineData("slight")]
    [InlineData("a bit")]
    [InlineData("somewhat")]
    [InlineData("a little")]
    [InlineData("only")]
    [InlineData("just")]
    [InlineData("steady")]
    [InlineData("holding")]
    public void WorseningIsNeverSoftened(string softener)
    {
        var request = SeatFacts.BodyMonthly(new BodyMonthFacts(
            [new SignalMonth("Weight", SignalUnit.Pounds, 182, 181, RisingIsWorse: true)]));

        Assert.DoesNotContain(softener, string.Join(" ", request.Facts) + " " + request.Fallback, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worse", request.Facts[0], StringComparison.Ordinal);
    }

    [Fact]
    public void WhenNothingMovedItSaysNothingMoved()
    {
        var all = SeatFacts.BodyMonthly(new BodyMonthFacts(
        [
            new SignalMonth("Steps", SignalUnit.Steps, 8000, 8100, RisingIsWorse: false),
            new SignalMonth("Weight", SignalUnit.Pounds, 181, 181, RisingIsWorse: true),
        ]));
        Assert.Equal("All signals are close to last month.", all.Fallback);

        var single = SeatFacts.BodyMonthly(new BodyMonthFacts([new SignalMonth("Steps", SignalUnit.Steps, 8000, 8100, false, NamePlural: true)]));
        Assert.Equal("Steps are close to last month.", single.Fallback);
    }

    // ---- a signal is set against the owner's own past, only ----

    [Fact]
    public void ASignalIsComparedOnlyWithTheOwnersOwnPast()
    {
        // Structurally: the facts type has nowhere to hold a norm, a target or a population figure.
        var members = typeof(SignalFacts).GetProperties().Select(property => property.Name).ToList();
        foreach (var forbidden in new[] { "Norm", "Target", "Goal", "Recommended", "Population", "Range", "Healthy", "Typical" })
        {
            Assert.DoesNotContain(members, name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }

        // And in the words: the past is named, and nothing generic is.
        var told = string.Join(" ", SeatFacts.Signal(SleepSignal()).Facts);
        Assert.Contains("own October average", told, StringComparison.Ordinal);
        foreach (var generic in new[] { "normal", "typical", "healthy", "recommended", "population", "most people", "adults", "range", "target", "goal" })
        {
            Assert.DoesNotContain(generic, told, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- bad input is refused, not voiced ----

    [Fact]
    public void ABodyMonthWithNoSignalsAndNoDeviceNoteHasNothingToSay()
    {
        Assert.Throws<ArgumentException>(() => SeatFacts.BodyMonthly(new BodyMonthFacts([])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankNameIsRefused(string blank)
    {
        Assert.Throws<ArgumentException>(() => DinnerPlan.At(blank));
        Assert.Throws<ArgumentException>(() => SeatFacts.Today(new TodayFacts(new TimeOnly(5, 30), blank, 40, DinnerPlan.Out)));
        Assert.Throws<ArgumentException>(() => SeatFacts.Signal(new SignalFacts(blank, SignalUnit.Hours, 6, "today", 7, "October", false)));
        Assert.Throws<ArgumentException>(() => SeatFacts.Signal(new SignalFacts("Sleep", SignalUnit.Hours, 6, "today", 7, blank, false)));
    }

    [Fact]
    public void ANonNumberIsRefused()
    {
        Assert.Throws<ArgumentException>(() => SeatFacts.Signal(new SignalFacts("Sleep", SignalUnit.Hours, double.NaN, "today", 7, "October", false)));
        Assert.Throws<ArgumentException>(() => SeatFacts.BodyMonthly(new BodyMonthFacts(
            [new SignalMonth("Sleep", SignalUnit.Hours, 6, double.PositiveInfinity, false)])));
    }
}
