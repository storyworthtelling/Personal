using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using static Constitution.Tests.Calendar.InventedFamily;
using static Constitution.Tests.MealPlanning.MealData;

namespace Constitution.Tests.MealPlanning;

/// <summary>The rules an authored week is held to: each one has a week that breaks it and a week that does not.</summary>
public class MealRulesTests
{
    private static MealWeek Replace(MealWeek week, int index, DinnerChoice choice)
    {
        var dinners = week.Dinners.ToList();
        dinners[index] = choice;
        return MealWeek.Planned(week.WeekStart, dinners);
    }

    private static DinnerChoice CookedBy(DateOnly day, string recipe, string cook, int startHour, int startMinute, bool childrenDifferently = false)
    {
        var start = Edt(day.Month, day.Day, startHour, startMinute);
        if (day > new DateOnly(2026, 10, 31))
        {
            start = Est(day.Month, day.Day, startHour, startMinute);
        }

        var end = day > new DateOnly(2026, 10, 31) ? Est(day.Month, day.Day, 18) : Edt(day.Month, day.Day, 18);
        return DinnerChoice.Cooked(day, Id(recipe), cook, start, end, false, childrenDifferently, "Written by hand.");
    }

    private static List<MealViolation> Check(MealWeekRequest request, MealWeek week) => MealRules.Violations(request, week).ToList();

    [Fact]
    public void TheRulesWeekBreaksNoRule()
    {
        var request = Request(childrenHome: AllEvenings());

        Assert.Empty(Check(request, MealPlanner.Plan(request)));
    }

    [Fact]
    public void AWeekThatIsNotPlannedHasNoEvenings()
    {
        var request = Request();
        var stopped = MealWeek.NotPlanned(Monday, MealStop.BoxTooSmall, "Too few cards.");

        Assert.Contains(Check(request, stopped), v => v.Rule == MealRule.WrongEvenings);
    }

    [Fact]
    public void AWeekForAnotherMondayIsWrong()
    {
        var request = Request();
        var other = MealPlanner.Plan(request);
        var later = MealWeek.Planned(Monday.AddDays(7), other.Dinners.Select((d, i) => DinnerChoice.NothingPlanned(Monday.AddDays(7 + i), "None.")));

        Assert.Contains(Check(request, later), v => v.Rule == MealRule.WrongEvenings);
    }

    [Fact]
    public void ACardNotInTheBoxIsRefused()
    {
        var request = Request();
        var week = Replace(MealPlanner.Plan(request), 0, CookedBy(Monday, "ghost-dish", Sam, 17, 0));

        Assert.Contains(Check(request, week), v => v.Rule == MealRule.NotInTheBox && v.Day == Monday);
    }

    [Fact]
    public void ARepeatInTheSameWeekIsRefusedOnTheLaterEvening()
    {
        var request = Request();
        var planned = MealPlanner.Plan(request);
        var week = Replace(planned, 1, CookedBy(Monday.AddDays(1), planned.Dinners[0].Recipe!.Value.Value, Lee, 17, 0));

        var found = Check(request, week).Where(v => v.Rule == MealRule.RepeatsWithinFourteenDays).ToList();

        Assert.Equal([Monday.AddDays(1)], found.Select(v => v.Day));
    }

    [Fact]
    public void ARepeatOfRecentHistoryIsRefused()
    {
        var request = Request(history: Cooked(("tomato-pasta", 9)));
        var week = Replace(MealPlanner.Plan(request), 0, CookedBy(Monday, "tomato-pasta", Sam, 17, 0));

        Assert.Contains(Check(request, week), v => v.Rule == MealRule.RepeatsWithinFourteenDays && v.Day == Monday);
    }

    [Fact]
    public void ACookBlockOnBusyTimeIsRefused()
    {
        var request = Request(sam: [On(Monday, "Dentist", 16, 30, 17, 45)]);
        var week = Replace(MealPlanner.Plan(request), 0, CookedBy(Monday, "tomato-pasta", Sam, 17, 0));

        Assert.Contains(Check(request, week), v => v.Rule == MealRule.CookBlockedTime && v.Day == Monday);
    }

    [Fact]
    public void ACookBlockOnWorkHoursIsRefused()
    {
        var request = Request();
        var week = Replace(MealPlanner.Plan(request), 0, CookedBy(Monday, "tomato-pasta", Sam, 15, 0));

        Assert.Contains(Check(request, week), v => v.Rule == MealRule.CookBlockedTime && v.Day == Monday);
    }

    [Fact]
    public void ACookWhoIsOutAtDinnerIsRefused()
    {
        var request = Request(sam: [On(Monday, "Concert", 18, 30, 21, 0)]);
        var week = Replace(MealPlanner.Plan(request), 0, CookedBy(Monday, "tomato-pasta", Sam, 17, 0));

        Assert.Contains(Check(request, week), v => v.Rule == MealRule.CookNotAvailable && v.Day == Monday);
    }

    [Fact]
    public void ACookWhoIsNotAnAdultIsRefused()
    {
        var request = Request();
        var week = Replace(MealPlanner.Plan(request), 0, CookedBy(Monday, "tomato-pasta", Robin, 17, 0));

        Assert.Contains(Check(request, week), v => v.Rule == MealRule.CookNotAvailable && v.Day == Monday);
    }

    [Fact]
    public void ABlockThatEndsBeforeDinnerIsRefused()
    {
        var request = Request();
        var early = DinnerChoice.Cooked(Monday, Id("tomato-pasta"), Sam, Edt(10, 26, 16, 30), Edt(10, 26, 17, 30), false, false, "Early.");
        var week = Replace(MealPlanner.Plan(request), 0, early);

        Assert.Contains(Check(request, week), v => v.Rule == MealRule.CookBlockMisplaced && v.Day == Monday);
    }

    [Fact]
    public void ADishAnAdultSaidNeverAgainToIsRefusedWhileThatAdultIsHome()
    {
        var request = Request(neverAgain: Never("tomato-pasta", Sam));
        var week = Replace(MealPlanner.Plan(request), 0, CookedBy(Monday, "tomato-pasta", Lee, 17, 0));

        Assert.Contains(Check(request, week), v => v.Rule == MealRule.VetoedDish && v.Day == Monday);
    }

    [Fact]
    public void ThatDishIsFineOnTheEveningTheAdultIsAway()
    {
        var request = Request(sam: [On(Monday, "Concert", 18, 30, 21, 0)], neverAgain: Never("tomato-pasta", Sam));
        var week = Replace(MealPlanner.Plan(request), 0, CookedBy(Monday, "tomato-pasta", Lee, 17, 0));

        Assert.DoesNotContain(Check(request, week), v => v.Rule == MealRule.VetoedDish);
    }

    [Fact]
    public void AChildsNeverAgainNeedsADifferentMealForTheChild()
    {
        var request = Request(neverAgain: Never("tomato-pasta", Robin), childrenHome: AllEvenings());
        var planned = MealPlanner.Plan(request);

        var plain = Replace(planned, 0, CookedBy(Monday, "tomato-pasta", Sam, 17, 0));
        var different = Replace(planned, 0, CookedBy(Monday, "tomato-pasta", Sam, 17, 0, childrenDifferently: true));

        Assert.Contains(Check(request, plain), v => v.Rule == MealRule.VetoedDish);
        Assert.DoesNotContain(Check(request, different), v => v.Rule == MealRule.VetoedDish);
    }

    [Fact]
    public void LeftoversEatingOutAndNothingNeedNoCook()
    {
        var request = Request();
        var planned = MealPlanner.Plan(request);
        var week = Replace(Replace(Replace(planned, 0, DinnerChoice.Leftovers(Monday, "Last night's.")), 1, DinnerChoice.EatOut(Monday.AddDays(1), "Out.")), 2, DinnerChoice.NothingPlanned(Monday.AddDays(2), "Free."));

        Assert.Empty(Check(request, week));
    }
}
