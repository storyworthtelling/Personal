using Constitution.Domain.Calendar;
using Constitution.Domain.Household;
using Constitution.Domain.Settings;

namespace Constitution.Tests.HouseholdSettings;

public class HouseholdGroupTests
{
    private static HouseholdGroupModel OwnerModel() =>
        HouseholdGroupModel.Build(HouseholdData.Roster(), RosterRole.Owner,
            new HouseholdSetupRead.Configured(PlanningSetupFromRosterBuild()));

    private static PlanningSetup PlanningSetupFromRosterBuild() =>
        PlanningSetupFromRoster.Build(HouseholdData.Roster(), HouseholdData.Choices()).Setup!;

    [Fact]
    public void HeaderNamesPeopleAndBedtime()
    {
        var model = OwnerModel();

        Assert.Equal("Household 4 people · bedtime 7:30 pm", model.Header);
    }

    [Fact]
    public void EachPersonCarriesATag()
    {
        var tags = OwnerModel().People.ToDictionary(row => row.Name, row => row.Tag);

        Assert.Equal("adult", tags["Sam"]);
        Assert.Equal("child", tags["Robin"]);
    }

    [Fact]
    public void BirthdatesNeverListed()
    {
        var text = string.Join("|", OwnerModel().People.Select(row => $"{row.Name}{row.Tag}{row.MutedLine}"))
            + OwnerModel().Header + OwnerModel().Summary;

        Assert.DoesNotContain(Tests.HouseholdFile.RosterData.FakeYear, text);
        Assert.DoesNotContain(typeof(HouseholdPersonRow).GetProperties(), property => property.Name.Contains("Birth"));
    }

    [Fact]
    public void DislikesShowInOneMutedLine()
    {
        var roster = HouseholdData.Roster();
        var lines = new Dictionary<PersonId, string> { [new PersonId("lee")] = "Won't eat: cilantro" };

        var model = HouseholdGroupModel.Build(roster, RosterRole.Owner, new HouseholdSetupRead.NotSet("x"), lines);

        Assert.Equal("Won't eat: cilantro", model.People.Single(row => row.Name == "Lee").MutedLine);
        Assert.Null(model.People.Single(row => row.Name == "Sam").MutedLine);
    }

    [Fact]
    public void ReadOnlyOnThePartnersInstall()
    {
        var model = HouseholdGroupModel.Build(HouseholdData.Roster(), RosterRole.Partner,
            new HouseholdSetupRead.Configured(PlanningSetupFromRosterBuild()));

        Assert.False(model.CanAddPerson);
        Assert.Equal(RosterEditor.PartnerReadOnlySentence, model.ReadOnlyNote);
        Assert.All(model.People, row => Assert.False(row.Editable));
        Assert.All(model.Pickup, row => Assert.False(row.Editable));
    }

    [Fact]
    public void OwnerCanAddAndEdit()
    {
        var model = OwnerModel();

        Assert.True(model.CanAddPerson);
        Assert.Equal("Add a person", model.AddLabel);
        Assert.Null(model.ReadOnlyNote);
        Assert.All(model.People, row => Assert.True(row.Editable));
    }

    [Fact]
    public void NoRosterSaysSoPlainly()
    {
        var model = HouseholdGroupModel.Build(null, RosterRole.Owner, new HouseholdSetupRead.Configured(HouseholdData.Setup()));

        Assert.Equal(HouseholdGroupModel.NoHouseholdFileSentence, model.RosterStatus);
        Assert.Empty(model.People);
        Assert.False(model.CanAddPerson);
    }

    [Fact]
    public void NoRosterStillEditsPickupFromTheSetup()
    {
        var model = HouseholdGroupModel.Build(null, RosterRole.Owner, new HouseholdSetupRead.Configured(HouseholdData.Setup()));

        Assert.Equal(["Sam", "Lee"], model.Pickup.Select(row => row.Name));
        Assert.All(model.Pickup, row => Assert.True(row.Editable));
        Assert.True(model.Pickup[0].OnPickup);
    }

    [Fact]
    public void UnsetBedtimeShowsAssumed()
    {
        var model = HouseholdGroupModel.Build(null, RosterRole.Owner, new HouseholdSetupRead.NotSet("none"));

        Assert.True(model.Bedtime.IsAssumed);
        Assert.Equal("7:30 pm", model.Bedtime.Value);
        Assert.True(model.Dinner.IsAssumed);
        Assert.Equal("5:30 pm", model.Dinner.Value);
        Assert.Contains("assumed", model.Header);
    }

    [Fact]
    public void SetBedtimeIsNotAssumed()
    {
        var model = OwnerModel();

        Assert.False(model.Bedtime.IsAssumed);
        Assert.False(model.Dinner.IsAssumed);
        Assert.Equal("5:45 pm", model.Dinner.Value);
    }

    [Fact]
    public void UnsetCalendarsSayNotSet()
    {
        var model = HouseholdGroupModel.Build(null, RosterRole.Owner, new HouseholdSetupRead.Configured(HouseholdData.Setup()));

        Assert.Equal("Not set.", model.Calendars.Single(row => row.Label == "Lee").StatusText);
        Assert.Equal("Not set.", model.Calendars.Single(row => row.Key == "family").StatusText);
        Assert.NotEqual("Not set.", model.Calendars.Single(row => row.Label == "Sam").StatusText);
    }

    [Theory]
    [InlineData(0, 30, false)]
    [InlineData(0, 59, false)]
    [InlineData(1, 0, true)]
    [InlineData(19, 30, true)]
    public void BedtimeNeedsAnHourBefore(int hour, int minute, bool fine) =>
        Assert.Equal(fine, HouseholdGroupModel.CheckBedtime(new TimeOnly(hour, minute)) is null);

    [Fact]
    public void DinnerMustEndBeforeMidnight()
    {
        Assert.Null(HouseholdGroupModel.CheckDinner(new TimeOnly(17, 30), TimeSpan.FromMinutes(60)));
        Assert.NotNull(HouseholdGroupModel.CheckDinner(new TimeOnly(23, 30), TimeSpan.FromMinutes(60)));
        Assert.NotNull(HouseholdGroupModel.CheckDinner(new TimeOnly(17, 30), TimeSpan.Zero));
    }
}

public class HouseholdGapWordsTests
{
    [Fact]
    public void NothingMissingSaysNothing() => Assert.Equal(string.Empty, HouseholdGroupModel.Describe([]));

    [Fact]
    public void GapsAreSaidInWords()
    {
        var text = HouseholdGroupModel.Describe([SetupGap.FamilyCalendarNotSet, SetupGap.NoAdultDeliversHere]);

        Assert.Equal("Still missing: the family calendar, which adult this phone delivers for.", text);
    }

    [Fact]
    public void EveryGapHasWords()
    {
        foreach (var gap in Enum.GetValues<SetupGap>())
        {
            Assert.NotEqual(string.Empty, HouseholdGroupModel.Describe([gap]));
        }
    }
}
