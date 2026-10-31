using Constitution.Domain.Calendar;
using Constitution.Domain.Household;
using Constitution.Domain.Settings;

namespace Constitution.Tests.HouseholdSettings;

public class PlanningSetupFromRosterTests
{
    [Fact]
    public void RosterAdultsBecomeTheSetupsAdults()
    {
        var build = PlanningSetupFromRoster.Build(HouseholdData.Roster(), HouseholdData.Choices());

        var setup = build.Setup!;
        Assert.Equal(["Lee", "Sam"], setup.Adults.Select(adult => adult.Name).Order());
        Assert.Equal("sam@example.com", setup.CalendarOf("Sam"));
        Assert.Equal(["sam@example.com"], setup.FindAdult("Sam")!.Addresses);
        Assert.True(setup.FindAdult("Sam")!.OnPickup);
        Assert.False(setup.FindAdult("Lee")!.OnPickup);
        Assert.Equal(["Sam"], setup.DeliverFor);
        Assert.False(setup.NoChildren);
        Assert.Equal(new TimeOnly(19, 30), setup.Bedtime);
        Assert.Equal(TimeSpan.FromMinutes(45), setup.DinnerLength);
        Assert.True(build.IsComplete);
    }

    [Fact]
    public void ItRoundTripsThroughTheCodec()
    {
        var setup = PlanningSetupFromRoster.Build(HouseholdData.Roster(), HouseholdData.Choices()).Setup!;

        var read = Assert.IsType<HouseholdSetupRead.Configured>(PlanningSetupCodec.Read(PlanningSetupCodec.Write(setup)));

        Assert.Equal(PlanningSetupCodec.Write(setup), PlanningSetupCodec.Write(read.Setup));
        Assert.Equal("America/Chicago", read.Setup.ZoneId);
    }

    [Fact]
    public void NothingChosenStaysNullNotDefaulted()
    {
        var build = PlanningSetupFromRoster.Build(HouseholdData.Roster(), new HouseholdChoices { DeliversHere = "Sam" });

        Assert.Null(build.Setup!.Bedtime);
        Assert.Null(build.Setup.DinnerStart);
        Assert.Null(build.Setup.ZoneId);
        Assert.Null(build.Setup.CalendarOf("Sam"));
    }

    [Fact]
    public void MissingCalendarsAreTypedGaps()
    {
        var build = PlanningSetupFromRoster.Build(HouseholdData.Roster(), new HouseholdChoices { DeliversHere = "Sam" });

        Assert.Contains(SetupGap.FamilyCalendarNotSet, build.Gaps);
        Assert.Contains(SetupGap.ChildrenCalendarsNotSet, build.Gaps);
        Assert.False(build.IsComplete);
        Assert.True(build.CanWrite);
    }

    [Fact]
    public void SayingNoneClosesTheGap()
    {
        var choices = new HouseholdChoices { DeliversHere = "Sam", NoFamilyCalendar = true, ChildrenCalendarIds = ["school@example.com"] };

        Assert.DoesNotContain(SetupGap.FamilyCalendarNotSet, PlanningSetupFromRoster.Build(HouseholdData.Roster(), choices).Gaps);
    }

    [Fact]
    public void NoDeliveringAdultIsAGap()
    {
        var build = PlanningSetupFromRoster.Build(HouseholdData.Roster(), HouseholdData.Choices() with { DeliversHere = null });

        Assert.Contains(SetupGap.NoAdultDeliversHere, build.Gaps);
    }

    [Fact]
    public void ANonAdultDeliveryIsAGap()
    {
        var build = PlanningSetupFromRoster.Build(HouseholdData.Roster(), HouseholdData.Choices() with { DeliversHere = "Robin" });

        Assert.Contains(SetupGap.DeliversHereIsNotAnAdult, build.Gaps);
    }

    [Fact]
    public void NoAdultsBuildsNothing()
    {
        var build = PlanningSetupFromRoster.Build(new RosterView([], []), HouseholdData.Choices());

        Assert.Null(build.Setup);
        Assert.Equal([SetupGap.NoAdults], build.Gaps);
    }

    [Fact]
    public void NoChildrenNeedsNoChildrenCalendar()
    {
        var adultsOnly = new RosterView(HouseholdData.Roster().Adults, []);

        var build = PlanningSetupFromRoster.Build(adultsOnly, HouseholdData.Choices() with { ChildrenCalendarIds = [] });

        Assert.True(build.Setup!.NoChildren);
        Assert.DoesNotContain(SetupGap.ChildrenCalendarsNotSet, build.Gaps);
    }

    [Fact]
    public void ApplyKeepsAdultsAndTakesChoices()
    {
        var choices = new HouseholdChoices
        {
            Bedtime = new TimeOnly(20, 0),
            DeliversHere = "Lee",
            AdultCalendars = new Dictionary<string, string> { ["Lee"] = "lee@example.com" },
            Pickup = new Dictionary<string, bool> { ["Lee"] = false },
        };

        var setup = PlanningSetupFromRoster.Apply(HouseholdData.Setup(), choices).Setup!;

        Assert.Equal("lee@example.com", setup.CalendarOf("Lee"));
        Assert.False(setup.FindAdult("Lee")!.OnPickup);
        Assert.Equal(["Lee"], setup.DeliverFor);
        Assert.Equal(new TimeOnly(20, 0), setup.Bedtime);
        Assert.Equal(["sam@example.com"], setup.FindAdult("Sam")!.Addresses);
    }

    [Fact]
    public void FromReadsBackWhatASetupHolds()
    {
        var choices = HouseholdChoices.From(PlanningSetupFromRoster.Build(HouseholdData.Roster(), HouseholdData.Choices()).Setup!);

        Assert.Equal(new TimeOnly(19, 30), choices.Bedtime);
        Assert.Equal("Sam", choices.DeliversHere);
        Assert.Equal("sam@example.com", choices.AdultCalendars["Sam"]);
        Assert.Equal("family@example.com", choices.FamilyCalendarId);
    }
}
