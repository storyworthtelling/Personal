using Constitution.Domain.Delivery;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Tests.MealWorker;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.SharedWeek.SharedWeekData;

namespace Constitution.Tests.SharedWeek;

/// <summary>Whose week a phone works from, and when a week another phone planned has something for this phone's tablet.</summary>
public class SharedWeekRuleTests
{
    private static readonly DateTimeOffset InsideSundaysHour = Edt(10, 25, 16, 30);

    private static WeekPlanSet SetOf(params (SharedWeekPlan Plan, string Writer)[] plans) =>
        new(plans.Select(item => new HeldWeekPlan(item.Plan, item.Writer)).ToList(), []);

    private static DeliveredMealDocument Sent(string name, DeliveryKind kind) => new(name, kind, Lee, new DateOnly(2026, 10, 25), Edt(10, 25, 17, 15), "fingerprint");

    private static string SheetName => DeliveredName.For(DeliveryKind.Meals, new DateOnly(2026, 10, 25));

    private static string SundayRecipeName => DeliveredName.For(DeliveryKind.Recipe, new DateOnly(2026, 10, 25), Lee);

    // ---- whose ----

    [Fact]
    public void OnlyAnotherPhonesPlansAreForThisPhone()
    {
        var set = SetOf((Plan(), OwnerPhone), (Plan() with { Stored = Stored() with { Week = MealWorkerData.WeekOf(MealWorkerData.NextSunday) } }, PartnerPhone));

        var theirs = SharedWeeks.FromOthers(set, PartnerPhone);

        Assert.Equal(new DateOnly(2026, 10, 25), Assert.Single(theirs).WeekStart);
    }

    [Fact]
    public void AWeekAnotherPhonePublishedTakesTheLocalCopyOffThisPhonesKeptWeeks()
    {
        var local = MealWorkerData.Kept(new DateOnly(2026, 10, 25), MealWorkerData.NextSunday);
        var set = SetOf((Plan(), OwnerPhone));

        var kept = Assert.IsType<MealWeekRecordRead.Held>(SharedWeeks.KeptAfter(local, set, PartnerPhone));

        Assert.Equal(MealWorkerData.NextSunday, Assert.Single(kept.Record.Weeks).WeekStart);
    }

    [Fact]
    public void AWeekThisPhoneMirroredStaysOnItsKeptWeeks()
    {
        var local = MealWorkerData.Kept(new DateOnly(2026, 10, 25));
        var set = SetOf((Plan(), PartnerPhone));

        var kept = Assert.IsType<MealWeekRecordRead.Held>(SharedWeeks.KeptAfter(local, set, PartnerPhone));

        Assert.Single(kept.Record.Weeks);
    }

    [Fact]
    public void AnUnreadableOrEmptyLocalRecordIsLeftAsItIs()
    {
        var set = SetOf((Plan(), OwnerPhone));
        var unreadable = new MealWeekRecordRead.Unreadable("Damaged.");

        Assert.Same(unreadable, SharedWeeks.KeptAfter(unreadable, set, PartnerPhone));
        var none = new MealWeekRecordRead.NothingYet();
        Assert.Same(none, SharedWeeks.KeptAfter(none, set, PartnerPhone));
    }

    // ---- due ----

    [Fact]
    public void ARecipeInItsHourIsDueForTheCooksTablet()
    {
        var due = SharedWeeks.Due([Plan()], InsideSundaysHour, Lee, _ => null);

        var week = Assert.Single(due);
        Assert.Equal(new DateOnly(2026, 10, 25), week.Week);
    }

    [Fact]
    public void ARecipeIsNotDueForTheOtherAdultsTablet()
    {
        var due = SharedWeeks.Due([Plan()], InsideSundaysHour, Sam, name => name == SheetName ? Sent(SheetName, DeliveryKind.Meals) : null);

        Assert.Empty(due);
    }

    [Fact]
    public void TheSheetIsDueBeforeTheDeadlineWhenItIsNotOnTheTablet()
    {
        var due = SharedWeeks.Due([Plan()], Edt(10, 22, 12), Lee, _ => null);

        var week = Assert.Single(due);
        Assert.True(week.Sheet);
    }

    [Fact]
    public void TheSheetIsNotDueOnceItIsOnTheTablet()
    {
        var due = SharedWeeks.Due([Plan()], Edt(10, 22, 12), Lee, name => name == SheetName ? Sent(name, DeliveryKind.Meals) : null);

        Assert.Empty(due);
    }

    [Fact]
    public void TheSheetIsNotDueOnceTheDeadlineHasPassed()
    {
        var due = SharedWeeks.Due([Plan()], Deadline, Lee, _ => null);

        Assert.DoesNotContain(due, week => week.Sheet);
    }

    [Fact]
    public void ARecipeAlreadyOnTheTabletIsNotDueAgain()
    {
        var due = SharedWeeks.Due(
            [Plan()], InsideSundaysHour, Lee, name => name == SheetName ? Sent(name, DeliveryKind.Meals) : name == SundayRecipeName ? Sent(name, DeliveryKind.Recipe) : null);

        Assert.Empty(due);
    }

    [Fact]
    public void ARecipeInItsHourWithTheSheetStillWaitingAsksForBoth()
    {
        var due = SharedWeeks.Due([Plan() with { Deadline = InsideSundaysHour.AddHours(1) }], InsideSundaysHour, Lee, name => null);

        Assert.True(Assert.Single(due).Sheet);
    }

    [Fact]
    public void NoTabletMeansNothingIsDue()
    {
        Assert.Empty(SharedWeeks.Due([Plan()], InsideSundaysHour, null, _ => null));
        Assert.Empty(SharedWeeks.Due([Plan()], InsideSundaysHour, " ", _ => null));
    }

    [Fact]
    public void NoPlansMeansNothingIsDue()
    {
        Assert.Empty(SharedWeeks.Due([], InsideSundaysHour, Lee, _ => null));
    }

    [Fact]
    public void TheRecordOfDeliveriesIsOnlyAskedWhatTheLookNeeds()
    {
        var asked = new List<string>();

        SharedWeeks.Due([Plan()], Edt(10, 22, 12), Lee, name =>
        {
            asked.Add(name);
            return null;
        });

        Assert.Equal([SheetName], asked);
    }
}
