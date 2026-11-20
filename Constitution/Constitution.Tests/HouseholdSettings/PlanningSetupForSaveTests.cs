using Constitution.Domain.Calendar;
using Constitution.Domain.Settings;

namespace Constitution.Tests.HouseholdSettings;

/// <summary>
/// What the Household group writes when a choice is saved. The adults come from the household file's roster whenever there is one, so a
/// phone whose setup file names nobody yet can still save a calendar or a time (the bug: every save was refused with "no adult to plan
/// for", and the page redrew blank).
/// </summary>
public class PlanningSetupForSaveTests
{
    private static readonly HouseholdSetupRead NoFile = new HouseholdSetupRead.NotSet("No setup file yet.");

    [Fact]
    public void WithARosterAndNoSetupFileTheChoiceIsSaved()
    {
        var choices = new HouseholdChoices { FamilyCalendarId = "family@example.com" };

        var build = PlanningSetupFromRoster.ForSave(HouseholdData.Roster(), NoFile, choices);

        Assert.True(build.CanWrite);
        Assert.Equal("family@example.com", build.Setup!.FamilyCalendarId);
        Assert.Equal(["Lee", "Sam"], build.Setup.Adults.Select(adult => adult.Name).Order());
        Assert.DoesNotContain(SetupGap.NoAdults, build.Gaps);
    }

    [Fact]
    public void WithARosterAndNoSetupFileATimeIsSaved()
    {
        var build = PlanningSetupFromRoster.ForSave(HouseholdData.Roster(), NoFile, new HouseholdChoices { DinnerStart = new TimeOnly(18, 0) });

        Assert.Equal(new TimeOnly(18, 0), build.Setup!.DinnerStart);
    }

    [Fact]
    public void TheRosterNamesTheAdultsOverASavedSetup()
    {
        var saved = new HouseholdSetupRead.Configured(new PlanningSetup([new HouseholdAdult("Juniper", null)]));

        var build = PlanningSetupFromRoster.ForSave(HouseholdData.Roster(), saved, HouseholdData.Choices());

        Assert.Equal(["Lee", "Sam"], build.Setup!.Adults.Select(adult => adult.Name).Order());
        Assert.Equal("lee@example.com", build.Setup.CalendarOf("Lee"));
    }

    [Fact]
    public void WithNoRosterTheSavedSetupsAdultsTakeTheChoice()
    {
        var saved = new HouseholdSetupRead.Configured(HouseholdData.Setup());

        var build = PlanningSetupFromRoster.ForSave(null, saved, new HouseholdChoices { FamilyCalendarId = "family@example.com", DeliversHere = "Sam" });

        Assert.Equal(["Lee", "Sam"], build.Setup!.Adults.Select(adult => adult.Name).Order());
        Assert.Equal("family@example.com", build.Setup.FamilyCalendarId);
    }

    [Fact]
    public void WithNoRosterAndNoSetupThereIsNobodyToSaveFor()
    {
        var build = PlanningSetupFromRoster.ForSave(null, NoFile, new HouseholdChoices { FamilyCalendarId = "family@example.com" });

        Assert.False(build.CanWrite);
        Assert.Equal([SetupGap.NoAdults], build.Gaps);
    }
}
