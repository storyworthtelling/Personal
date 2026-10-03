using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// Which of the people has a Google account, and therefore which of them can be routed by one.
///
/// <para>The owner's correction: the parents have addresses; the girls do not. Their days reach the
/// page on calendars shared between the parents, so a child's column is filled by the calendar an
/// event came from, never by an invitation — they are not invited to anything because they have no
/// account to invite.</para>
///
/// <para>It matters because an address field offered for a child is a field that cannot work.
/// Whatever is typed into it never matches a guest, nothing errors, and the events go on landing
/// wherever they were landing before — the exact failure that looks like a settings screen that
/// does nothing.</para>
/// </summary>
public class PersonIdentityTests
{
    [Fact]
    public void TheParentsHaveMailboxes()
    {
        Assert.True(Household.Layout.HasAccount(Household.Alex));
        Assert.True(Household.Layout.HasAccount(Household.Sam));
    }

    [Fact]
    public void TheChildrenDoNot()
    {
        Assert.False(Household.Layout.HasAccount(Household.Riley));
        Assert.False(Household.Layout.HasAccount(Household.Jo));
    }

    /// <summary>Watch is a column, not a person, so there is nobody there to have an address.</summary>
    [Fact]
    public void TheWatchColumnIsNotAPerson()
    {
        Assert.False(Household.Layout.HasAccount(PersonColumn.Watch));
    }

    [Fact]
    public void TheAddressableOnesAreTheParentsInColumnOrder()
    {
        Assert.Equal([Household.Alex, Household.Sam], Household.Layout.WithAccounts);
    }
}
