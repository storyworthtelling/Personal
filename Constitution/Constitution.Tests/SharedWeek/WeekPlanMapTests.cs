using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Storyworthtelling.Household;
using static Constitution.Tests.SharedWeek.SharedWeekData;

namespace Constitution.Tests.SharedWeek;

/// <summary>The <c>week-plan</c> record: one kept week, keyed by its Sunday, carrying what the other phone needs to print and nothing else.</summary>
public class WeekPlanMapTests
{
    private static readonly string[] FrozenFields = ["daily", "deadline", "dishes", "extra", "format", "planned", "week", "weekStart"];

    [Fact]
    public void TheKeyIsTheKindAndTheSunday()
    {
        Assert.Equal("week-plan/2026-10-25", WeekPlanMaps.WeekPlan.KeyOf(Plan()).Value);
        Assert.Equal(ConstitutionKinds.WeekPlan, WeekPlanMaps.WeekPlan.Kind);
    }

    [Fact]
    public void TheFieldNamesAreFrozen()
    {
        var names = WeekPlanMaps.WeekPlan.FieldsOf(Plan(daily: Daily())).Keys.Order(StringComparer.Ordinal).ToList();

        Assert.Equal(FrozenFields, names);
    }

    [Fact]
    public void TheFormatWordIsFrozen()
    {
        Assert.Equal("1", WeekPlanMaps.WeekPlan.FieldsOf(Plan())["format"]);
        Assert.Equal("2026-10-25", WeekPlanMaps.WeekPlan.FieldsOf(Plan())["weekStart"]);
    }

    [Fact]
    public void NothingPrivateHasAFieldToLiveIn()
    {
        foreach (var name in FrozenFields)
        {
            Assert.DoesNotContain("birth", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pass", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("key", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ADinnerWeekRoundTripsWithCardsBlocksAndKinds()
    {
        var document = Put(Plan(["Tuesday dinner"], Daily()));

        var read = WeekPlanMaps.Read(document);

        var held = Assert.Single(read.Plans);
        Assert.Empty(read.Skipped);
        Assert.Equal(OwnerPhone, held.Writer);
        var week = held.Plan.Stored.Week;
        Assert.Equal(DocsDinners(), week.Dinners.Select(dinner => (dinner.Kind, dinner.Recipe?.Value, dinner.Cook, dinner.CookStart, dinner.CookEnd, dinner.Reason)));
        Assert.Equal(Deadline, held.Plan.Deadline);
        Assert.Equal(["Tuesday dinner"], held.Plan.ExtraLines);
        Assert.Equal(WeekPlanMaps.WeekPlan.FieldsOf(Plan(["Tuesday dinner"], Daily())), WeekPlanMaps.WeekPlan.FieldsOf(held.Plan));
        Assert.Equal(Daily().WeekdayBreakfasts, held.Plan.Daily!.WeekdayBreakfasts, LinesAreEqual);
        Assert.Equal(Daily().Lunchbox, held.Plan.Daily.Lunchbox);
        Assert.Equal(Stored().PlannedAt, held.Plan.Stored.PlannedAt);
        Assert.Equal(Stored().Dishes, held.Plan.Stored.Dishes);
    }

    private static bool LinesAreEqual(Constitution.Domain.Documents.MealSheetLine left, Constitution.Domain.Documents.MealSheetLine right) => left == right;

    private static IEnumerable<(DinnerKind, string?, string?, DateTimeOffset?, DateTimeOffset?, string)> DocsDinners() =>
        Stored().Week.Dinners.Select(dinner => (dinner.Kind, dinner.Recipe?.Value, dinner.Cook, dinner.CookStart, dinner.CookEnd, dinner.Reason));

    [Fact]
    public void ANothingPlannedReasonSurvives()
    {
        var week = MealWeek.Planned(
            Stored().WeekStart,
            Stored().Week.Dinners.Select((dinner, index) => index == 3 ? DinnerChoice.NothingPlanned(dinner.Day, "Not approved.") : dinner));
        var plan = Plan() with { Stored = Stored() with { Week = week } };

        var held = Assert.Single(WeekPlanMaps.Read(Put(plan)).Plans);

        var denied = held.Plan.Stored.Week.Dinners[3];
        Assert.Equal(DinnerKind.NothingPlanned, denied.Kind);
        Assert.Equal("Not approved.", denied.Reason);
    }

    [Fact]
    public void NoDailyMealsAndNoExtraLinesReadBackAsNone()
    {
        var held = Assert.Single(WeekPlanMaps.Read(Put(Plan())).Plans);

        Assert.Null(held.Plan.Daily);
        Assert.Empty(held.Plan.ExtraLines);
    }

    [Fact]
    public void AWeekThatWasNotPlannedHasNoRecord()
    {
        var notPlanned = MealWeek.NotPlanned(Stored().WeekStart, MealStop.BoxTooSmall, "Too small.");

        Assert.Throws<ArgumentException>(() => WeekPlanMaps.WeekPlan.FieldsOf(Plan() with { Stored = Stored() with { Week = notPlanned } }));
    }

    [Fact]
    public void ANewerFormatIsSkippedWithItsOwnReason()
    {
        var document = WithField(Put(Plan()), "format", "2");

        var read = WeekPlanMaps.Read(document);

        Assert.Empty(read.Plans);
        var skipped = Assert.Single(read.Skipped);
        Assert.Equal(WeekPlanSkipReason.UnknownVersion, skipped.Reason);
        Assert.Equal("week-plan/2026-10-25", skipped.Key.Value);
    }

    [Theory]
    [InlineData("week", "not json at all")]
    [InlineData("week", "{\"version\":1}")]
    [InlineData("planned", "yesterday")]
    [InlineData("deadline", "")]
    [InlineData("dishes", "{}")]
    [InlineData("extra", "[1]")]
    [InlineData("daily", "{\"weekendBreakfast\":3}")]
    [InlineData("weekStart", "2026-10-26")]
    public void ARecordThatCannotBeReadIsSkippedNotGuessed(string field, string value)
    {
        var read = WeekPlanMaps.Read(WithField(Put(Plan(daily: Daily())), field, value));

        Assert.Empty(read.Plans);
        Assert.Equal(WeekPlanSkipReason.Unreadable, Assert.Single(read.Skipped).Reason);
    }

    [Fact]
    public void ASkipSentenceQuotesNothingTheRecordHeld()
    {
        var read = WeekPlanMaps.Read(WithField(Put(Plan()), "week", "Lentil soup secret note"));

        var sentence = Assert.Single(read.Skipped).Sentence;
        Assert.DoesNotContain("Lentil", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void OneBadRecordDoesNotHideTheGoodOnes()
    {
        var next = Plan() with { Stored = Stored() with { Week = Stored().Week } };
        var document = Put(Plan());
        var other = HouseholdRecords.Put(
            HouseholdDocument.Empty,
            WeekPlanMaps.WeekPlan,
            next with { Stored = new StoredMealWeek(Constitution.Tests.MealWorker.MealWorkerData.WeekOf(new DateOnly(2026, 11, 1)), Stored().PlannedAt, []), Deadline = Deadline.AddDays(7) },
            At(11),
            OwnerPhone);
        var both = HouseholdMerge.Merge(WithField(document, "week", "{"), other);

        var read = WeekPlanMaps.Read(both);

        Assert.Equal(new DateOnly(2026, 11, 1), Assert.Single(read.Plans).Plan.WeekStart);
        Assert.Single(read.Skipped);
    }

    [Fact]
    public void ADeletedPlanIsNotRead()
    {
        var document = HouseholdRecords.Remove(Put(Plan()), WeekPlanMaps.WeekPlan, Plan(), At(30), OwnerPhone);

        var read = WeekPlanMaps.Read(document);

        Assert.Empty(read.Plans);
        Assert.Empty(read.Skipped);
    }

    [Fact]
    public void AFieldAnotherWriterAddedSurvivesAnEdit()
    {
        var document = WithField(Put(Plan()), "futureField", "kept");

        var edited = HouseholdRecords.Put(document, WeekPlanMaps.WeekPlan, Plan(["Monday dinner"]), At(40), OwnerPhone);

        Assert.Equal("kept", Record(edited).Fields["futureField"]);
        Assert.Equal(["Monday dinner"], Assert.Single(WeekPlanMaps.Read(edited).Plans).Plan.ExtraLines);
    }

    [Fact]
    public void ANewerStampFromTheLeaseHolderWinsTheMerge()
    {
        var older = Put(Plan(["Old line"]), 10, OwnerPhone);
        var newer = Put(Plan(["New line"]), 20, OwnerPhone);

        var merged = HouseholdMerge.Merge(older, newer);

        Assert.Equal(["New line"], Assert.Single(WeekPlanMaps.Read(merged).Plans).Plan.ExtraLines);
        Assert.True(merged.ContentEquals(HouseholdMerge.Merge(newer, older)));
    }
}
