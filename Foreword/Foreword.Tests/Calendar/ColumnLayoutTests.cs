using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// The columns this account prints, in order. The owner sets them up in Foreword; nothing in the code
/// says how many there are or who they are.
/// </summary>
public class ColumnLayoutTests
{
    [Fact]
    public void WatchPrintsLastAfterThePeople()
    {
        Assert.Equal(
            [Household.Alex, Household.Sam, Household.Riley, Household.Jo, PersonColumn.Watch],
            Household.Layout.Printed);
    }

    [Fact]
    public void OnlyThePeopleWithAnAccountCanBeInvited()
    {
        Assert.True(Household.Layout.HasAccount(Household.Sam));
        Assert.False(Household.Layout.HasAccount(Household.Riley));
    }

    /// <summary>
    /// A fresh install has chosen nothing yet, and still prints: one column that is whoever is
    /// holding the phone, until they set up more.
    /// </summary>
    [Fact]
    public void AnInstallWithNoColumnsYetPrintsOneForItsOwner()
    {
        var unset = ColumnLayout.Unset;

        Assert.Single(unset.People);
        Assert.Equal(unset.People, unset.WithAccounts);
        Assert.Equal([unset.People[0], PersonColumn.Watch], unset.Printed);
    }

    [Fact]
    public void WatchIsNeverOneOfThePeople()
    {
        var layout = new ColumnLayout([Household.Alex, PersonColumn.Watch], [Household.Alex]);

        Assert.Equal([Household.Alex], layout.People);
        Assert.Equal([Household.Alex, PersonColumn.Watch], layout.Printed);
    }
}
