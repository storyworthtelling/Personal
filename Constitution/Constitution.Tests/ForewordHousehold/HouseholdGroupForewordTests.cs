using Constitution.Domain.Calendar;
using Constitution.Domain.Household;
using Constitution.Domain.Settings;
using Constitution.Tests.HouseholdSettings;

namespace Constitution.Tests.ForewordHousehold;

/// <summary>Once Foreword holds the household, Constitution's Household group shows it and says where it is changed.</summary>
public class HouseholdGroupForewordTests
{
    private static HouseholdGroupModel Model(bool forewordOwns) =>
        HouseholdGroupModel.Build(HouseholdData.Roster(), RosterRole.Owner,
            new HouseholdSetupRead.Configured(PlanningSetupFromRoster.Build(HouseholdData.Roster(), HouseholdData.Choices()).Setup!),
            forewordOwns: forewordOwns);

    [Fact]
    public void ThePeopleAndTheirPickupAreReadOnlyAndSaySo()
    {
        var model = Model(forewordOwns: true);

        Assert.True(model.ForewordOwns);
        Assert.False(model.CanAddPerson);
        Assert.All(model.People, row => Assert.False(row.Editable));
        Assert.All(model.Pickup, row => Assert.False(row.Editable));
        Assert.Equal(HouseholdGroupModel.ForewordOwnsSentence, model.ReadOnlyNote);
        Assert.Contains("Foreword", model.ReadOnlyNote);
    }

    [Fact]
    public void WithoutForewordTheOwnerStillEdits()
    {
        var model = Model(forewordOwns: false);

        Assert.False(model.ForewordOwns);
        Assert.True(model.CanAddPerson);
        Assert.Null(model.ReadOnlyNote);
    }
}
