using Constitution.Domain.Calendar;

namespace Constitution.Tests.Setups;

public class PlanningSetupTests
{
    [Fact]
    public void ACalendarIsFoundByNameWithoutCase()
    {
        var setup = HouseholdData.Setup();

        Assert.Equal("sam@example.com", setup.CalendarOf("sam"));
        Assert.Equal("lee@example.com", setup.CalendarOf(" LEE "));
    }

    [Fact]
    public void AStrangerHasNoCalendar()
    {
        Assert.Null(HouseholdData.Setup().CalendarOf("Robin"));
        Assert.Null(HouseholdData.Setup().FindAdult("Robin"));
    }

    [Fact]
    public void AnAdultWithNoCalendarIdHasNone()
    {
        var setup = new PlanningSetup([new HouseholdAdult("Sam", "  ")]);

        Assert.Null(setup.CalendarOf("Sam"));
    }

    [Fact]
    public void ThePagesGoToWhoseTabletThisIs()
    {
        Assert.Equal(["Sam"], HouseholdData.Setup().DeliverFor);
    }
}
