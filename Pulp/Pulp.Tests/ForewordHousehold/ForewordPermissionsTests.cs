using Pulp.Domain.Household;
using Storyworthtelling.Household;

namespace Pulp.Tests.ForewordHousehold;

/// <summary>Once Foreword holds the household, who lives here is changed there; a child's viewing rules stay Pulp's.</summary>
public class ForewordPermissionsTests
{
    [Theory]
    [InlineData(HouseholdFileRole.Owner)]
    [InlineData(HouseholdFileRole.SharedWith)]
    [InlineData(HouseholdFileRole.Unknown)]
    public void PeopleAreForewordsAndChildrensRulesStayHere(HouseholdFileRole role)
    {
        var rights = HouseholdPermissions.For(role, forewordOwns: true);

        Assert.False(rights.CanAddOrRemoveViewers);
        Assert.False(rights.CanRenameOwnViewer);
        Assert.True(rights.CanEditChildren);
        Assert.Contains("Foreword", rights.Note);
    }

    [Fact]
    public void TheOwnerStillDecidesTheServices()
    {
        Assert.True(HouseholdPermissions.For(HouseholdFileRole.Owner, forewordOwns: true).CanEditServices);
        Assert.False(HouseholdPermissions.For(HouseholdFileRole.SharedWith, forewordOwns: true).CanEditServices);
    }

    [Fact]
    public void WithoutForewordNothingChanges()
    {
        Assert.Equal(HouseholdPermissions.For(HouseholdFileRole.Owner), HouseholdPermissions.For(HouseholdFileRole.Owner, forewordOwns: false));
    }
}
