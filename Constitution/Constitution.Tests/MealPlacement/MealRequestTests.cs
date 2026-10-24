using System.Text.RegularExpressions;
using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Placement;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlacement.MealPlacementData;
using MealCalendar = Constitution.Domain.Placement.MealPlacement;

namespace Constitution.Tests.MealPlacement;

/// <summary>
/// C-6.4.1 · what a week of dinners asks the placement engine for. Pure: a week, the household's rules and the box in, household
/// events out. Nothing is placed here; the engine tests below put these through the real engine.
/// </summary>
public class MealRequestTests
{
    private static readonly Regex DateShape = new(@"\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{6,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static MealPlacementOptions Slow(params Recipe[] cards) =>
        new() { SlowCookerCards = cards.Select(card => card.Id).ToHashSet() };

    private static IReadOnlyList<HouseholdEventRequest> Requests(MealWeek week, MealPlacementOptions? options = null) =>
        MealCalendar.Requests(week, House, Cards, options);

    // ---- the cook block ----

    [Fact]
    public void ACookedDinnerIsOneCookEvent()
    {
        var made = Assert.Single(Requests(WeekOf(CookedOn(1, Chili, Sam, 45))));

        Assert.Equal("Cook: chili", made.Title);
        Assert.Equal(new[] { Sam }, made.People);
        Assert.Equal(TimeSpan.FromMinutes(45), made.Length);
        var window = Assert.Single(made.Windows);
        Assert.Equal(Edt(10, 27, 17, 15), window.Start);
        Assert.Equal(Edt(10, 27, 18), window.End);
    }

    [Fact]
    public void OnlyCookedDinnersMakeEvents()
    {
        var week = WeekOf(
            CookedOn(0, Pasta, Lee, 30),
            DinnerChoice.Leftovers(Monday.AddDays(1), "Leftovers from the evening before."),
            DinnerChoice.EatOut(Monday.AddDays(2), "Dinner out."));

        var made = Assert.Single(Requests(week));

        Assert.Equal("Cook: tomato pasta", made.Title);
    }

    [Fact]
    public void AWeekNotPlannedMakesNoEvents()
    {
        var week = MealWeek.NotPlanned(Monday, MealStop.BoxTooSmall, "The recipe box has 3 of 10 cards, so no dinners are planned yet.");

        Assert.Empty(Requests(week));
    }

    [Fact]
    public void TheBlockEndsExactlyAtDinnerEveryEvening()
    {
        // The Sunday is the first evening on standard time; the dinner is still six on the clock.
        var week = WeekOf(
            CookedOn(0, Chili, Sam, 45),
            CookedOn(1, Pasta, Lee, 30),
            CookedOn(2, Stew, Sam, 120),
            CookedOn(3, Chili, Lee, 45),
            CookedOn(4, Pasta, Sam, 30),
            CookedOn(5, Stew, Lee, 120),
            CookedOn(6, Chili, Sam, 45));

        var made = Requests(week);

        Assert.Equal(7, made.Count);
        for (var evening = 0; evening < made.Count; evening++)
        {
            var window = Assert.Single(made[evening].Windows);
            Assert.Equal(DinnerStart(evening), window.End);
            Assert.Equal(made[evening].Length, window.End - window.Start);
        }
    }

    [Fact]
    public void NoFamilyDinnerEventIsMade()
    {
        // Dinner is the household's never-placed window, not an event: the PBI asks for the block that ends at it.
        var made = Requests(WeekOf(CookedOn(0, Chili, Sam, 45), CookedOn(1, Pasta, Lee, 30)), Slow(Chili));

        Assert.DoesNotContain(made, request => request.Title.Contains("dinner", StringComparison.OrdinalIgnoreCase));
        Assert.All(made, request => Assert.Single(request.People));
        Assert.All(made, request => Assert.All(request.Windows, window => Assert.True(window.End <= DinnerStart(EveningOf(window.Start)))));
    }

    [Fact]
    public void TheCooksNameIsTheAdultsOwnSpelling()
    {
        var made = Assert.Single(Requests(WeekOf(CookedOn(0, Chili, "lee", 45))));

        Assert.Equal(new[] { Lee }, made.People);
    }

    // ---- titles and keys ----

    [Fact]
    public void TheTitleIsCleanAndKeyless()
    {
        var request = RequestFor(WeekOf(CookedOn(0, Chili, Sam, 45), CookedOn(2, Stew, Lee, 120)), options: Slow(Stew));

        var wanted = PlacementEngine.Wanted(request);

        Assert.Equal(3, wanted.Count);
        Assert.Contains(wanted, item => item.Title == "Cook: chili" && item.Person == Sam);
        Assert.Contains(wanted, item => item.Title == "Cook: veggie stew" && item.Person == Lee);
        Assert.Contains(wanted, item => item.Title == "Start the slow cooker" && item.Person == Lee);
        Assert.All(wanted, item =>
        {
            Assert.DoesNotContain(item.Key.Value, item.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(PlacementKey.Property, item.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(DateShape, item.Title);
        });
    }

    [Fact]
    public void TheKeyIsTheEveningAndTheRole()
    {
        // Frozen: this text is stored on the owner's events, so changing it strands every meal already written.
        var request = RequestFor(WeekOf(CookedOn(1, Stew, Lee, 120)), options: Slow(Stew));

        var keys = PlacementEngine.Wanted(request).Select(item => item.Key.Value).Order().ToList();

        Assert.Equal(
            [$"c1:lee:household:{Week}:cook-tue", $"c1:lee:household:{Week}:slowcooker-tue"],
            keys);
    }

    [Fact]
    public void KeyOfNamesTheSameKeyAsTheEngine()
    {
        var request = RequestFor(WeekOf(CookedOn(3, Chili, Sam, 45), CookedOn(4, Stew, Lee, 120)), options: Slow(Stew));

        var wanted = PlacementEngine.Wanted(request);

        Assert.Contains(wanted, item => item.Key == MealCalendar.KeyOf(MealEventRole.Cook, Sam, Monday, Monday.AddDays(3)));
        Assert.Contains(wanted, item => item.Key == MealCalendar.KeyOf(MealEventRole.Cook, Lee, Monday, Monday.AddDays(4)));
        Assert.Contains(wanted, item => item.Key == MealCalendar.KeyOf(MealEventRole.StartSlowCooker, Lee, Monday, Monday.AddDays(4)));
    }

    [Fact]
    public void TheKeyNeverHoldsADate()
    {
        var week = WeekOf(
            CookedOn(0, Chili, Sam, 45),
            CookedOn(1, Stew, Lee, 120),
            CookedOn(2, Chili, Sam, 45),
            CookedOn(3, Stew, Lee, 120),
            CookedOn(4, Chili, Sam, 45),
            CookedOn(5, Stew, Lee, 120),
            CookedOn(6, Chili, Sam, 45));

        var keys = PlacementEngine.Wanted(RequestFor(week, options: Slow(Stew))).Select(item => item.Key.Value).ToList();

        Assert.Equal(10, keys.Count);
        Assert.All(keys, key =>
        {
            // The week is a six-digit number in the key by design; the part that names the event is what must hold no date.
            Assert.DoesNotMatch(DateShape, key.Split(':')[^1]);
            Assert.DoesNotContain("2026", key);
            Assert.DoesNotContain("-10-", key);
            Assert.DoesNotContain("-11-", key);
        });
    }

    [Fact]
    public void TheIdentityRepeatsFromWeekToWeek()
    {
        // The same evening of the next week is the same identity: only the week number in the key moves.
        var next = new DateOnly(2026, 11, 2);
        var week = MealWeek.Planned(
            next,
            Enumerable.Range(0, 7).Select(offset => offset == 1
                ? DinnerChoice.Cooked(next.AddDays(1), Chili.Id, Sam, Est(11, 3, 17, 15), Est(11, 3, 18), false, false, "From the box.")
                : DinnerChoice.NothingPlanned(next.AddDays(offset), "Nothing is planned.")));
        var thisWeek = Requests(WeekOf(CookedOn(1, Chili, Sam, 45)));

        var nextWeek = MealCalendar.Requests(week, House, Cards);

        Assert.Equal(Assert.Single(thisWeek).Identity, Assert.Single(nextWeek).Identity);
        Assert.Equal("cook-tue", Assert.Single(nextWeek).Identity);
    }

    // ---- the slow cooker ----

    [Fact]
    public void ASlowCookerMealAddsAMorningStart()
    {
        var made = Requests(WeekOf(CookedOn(2, Stew, Lee, 120)), Slow(Stew));

        Assert.Equal(2, made.Count);
        var cook = made[0];
        var start = made[1];
        Assert.Equal("Cook: veggie stew", cook.Title);
        Assert.Equal("Start the slow cooker", start.Title);
        Assert.Equal(new[] { Lee }, start.People);
        Assert.Equal(TimeSpan.FromMinutes(10), start.Length);
        Assert.Equal("slowcooker-wed", start.Identity);
    }

    [Fact]
    public void TheMorningWindowsComeInOrderOfPreference()
    {
        var start = Requests(WeekOf(CookedOn(2, Stew, Lee, 120)), Slow(Stew))[1];

        Assert.Equal(3, start.Windows.Count);
        Assert.Equal((Edt(10, 28, 7), Edt(10, 28, 9)), (start.Windows[0].Start, start.Windows[0].End));
        Assert.Equal((Edt(10, 28, 6), Edt(10, 28, 7)), (start.Windows[1].Start, start.Windows[1].End));
        Assert.Equal((Edt(10, 28, 9), Edt(10, 28, 12)), (start.Windows[2].Start, start.Windows[2].End));
    }

    [Fact]
    public void ACardNotMarkedSlowAddsNoMorningStart()
    {
        var made = Requests(WeekOf(CookedOn(2, Chili, Lee, 45)), Slow(Stew));

        Assert.Equal("Cook: chili", Assert.Single(made).Title);
    }

    [Fact]
    public void NothingIsSlowUnlessTheCallerSaysSo()
    {
        Assert.Single(Requests(WeekOf(CookedOn(2, Stew, Lee, 120))));
    }

    [Fact]
    public void CookBlocksComeBeforeMorningStarts()
    {
        // A fixed block claims its minutes before a flexible start can look for any.
        var week = WeekOf(CookedOn(0, Stew, Sam, 120), CookedOn(1, Stew, Lee, 120), CookedOn(2, Chili, Sam, 45));

        var titles = Requests(week, Slow(Stew)).Select(request => request.Title).ToList();

        Assert.Equal(
            ["Cook: veggie stew", "Cook: veggie stew", "Cook: chili", "Start the slow cooker", "Start the slow cooker"],
            titles);
    }

    [Fact]
    public void AMorningWindowIsCutShortOfTheBlock()
    {
        var options = Slow(Stew) with { SlowCookerMornings = [new DayPart(new TimeOnly(15, 0), TimeSpan.FromHours(3))] };

        var start = Requests(WeekOf(CookedOn(0, Stew, Sam, 120)), options)[1];

        var window = Assert.Single(start.Windows);
        Assert.Equal((Edt(10, 26, 15), Edt(10, 26, 16)), (window.Start, window.End));
    }

    [Fact]
    public void NoMorningLeftBeforeTheBlockIsRefused()
    {
        var options = Slow(Stew) with { SlowCookerMornings = [new DayPart(new TimeOnly(16, 30), TimeSpan.FromHours(1))] };

        var refusal = Assert.Throws<ArgumentException>(() => Requests(WeekOf(CookedOn(0, Stew, Sam, 120)), options));

        Assert.Contains("no room", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- a week that does not hold together is refused, not squared ----

    [Fact]
    public void ACookWhoIsNotAnAdultIsRefused()
    {
        var refusal = Assert.Throws<ArgumentException>(() => Requests(WeekOf(CookedOn(0, Chili, Robin, 45))));

        Assert.Contains("household's adults", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockNotEndingAtDinnerIsRefused()
    {
        var early = DinnerChoice.Cooked(Monday, Chili.Id, Sam, Edt(10, 26, 16, 30), Edt(10, 26, 17, 15), false, false, "From the box.");

        var refusal = Assert.Throws<ArgumentException>(() => Requests(WeekOf(early)));

        Assert.Contains("family dinner", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACardNotInTheBoxIsRefused()
    {
        var gone = Card("gone-card", "gone card", 45);

        var refusal = Assert.Throws<ArgumentException>(() => Requests(WeekOf(CookedOn(0, gone, Sam, 45))));

        Assert.Contains("not in the box", refusal.Message, StringComparison.Ordinal);
    }

    // ---- the options ----

    [Fact]
    public void ADayPartHasToRunForward()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DayPart(new TimeOnly(7, 0), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DayPart(new TimeOnly(23, 0), TimeSpan.FromHours(2)));
    }

    [Fact]
    public void TheStartHasToTakeSomeTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MealPlacementOptions { SlowCookerStartLength = TimeSpan.Zero });
    }

    [Fact]
    public void ThereHasToBeSomeMorning()
    {
        Assert.Throws<ArgumentException>(() => new MealPlacementOptions { SlowCookerMornings = [] });
    }
}
