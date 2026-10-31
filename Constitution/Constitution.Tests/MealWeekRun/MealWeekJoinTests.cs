using Constitution.Domain.Calendar;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Meals;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.MealWeekRun;

/// <summary>Joining the household's setup and the clock into the run's inputs.</summary>
public class MealWeekJoinTests
{
    private static HouseholdSetupRead Setup() => new HouseholdSetupRead.Configured(
        new PlanningSetup([new HouseholdAdult(Sam, "sam@example.com", DeliversHere: true), new HouseholdAdult(Lee, "lee@example.com")])
        {
            FamilyCalendarId = "family@example.com",
            NoChildren = true,
        });

    [Theory]
    [InlineData(10, 21, 12, 25)]
    [InlineData(10, 24, 23, 25)]
    [InlineData(10, 18, 9, 25)]
    public void TheWeekPlannedIsTheNextOne(int month, int day, int hour, int expectedDay)
    {
        Assert.Equal(new DateOnly(2026, 10, expectedDay), MealWeekJoin.WeekFor(Edt(month, day, hour), Zone));
    }

    [Fact]
    public void OnTheSundayItselfTheNextWeekIsTheOneAfter()
    {
        Assert.Equal(new DateOnly(2026, 11, 1), MealWeekJoin.WeekFor(Edt(10, 25, 12), Zone));
    }

    [Fact]
    public void ASetupMakesTheInputs()
    {
        var prepared = MealWeekJoin.Prepare(Setup(), null, Zone, Edt(10, 21, 12));

        var ready = Assert.IsType<MealWeekPreparation.Ready>(prepared);
        Assert.Equal(Sunday, ready.Inputs.WeekStart);
        Assert.Equal(Edt(10, 21, 12), ready.Inputs.Now);
        Assert.Equal([Sam, Lee], ready.Inputs.Rules.Adults.Select(adult => adult.Name));
    }

    [Fact]
    public void NoSetupIsNotConfiguredWithTheSetupsOwnSentence()
    {
        var prepared = MealWeekJoin.Prepare(new HouseholdSetupRead.NotSet("Nothing has been saved yet."), null, Zone, Edt(10, 21, 12));

        var stop = Assert.IsType<MealWeekPreparation.Stop>(prepared);
        Assert.Equal(MealNotPlannedBecause.NotConfigured, stop.Outcome.Because);
        Assert.Equal("Nothing has been saved yet.", stop.Outcome.Sentence);
    }
}
