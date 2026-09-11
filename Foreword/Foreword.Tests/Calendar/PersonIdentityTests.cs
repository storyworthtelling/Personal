using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// Which of the four has an email address, and therefore which of them can be routed by one.
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
        Assert.True(PersonIdentity.HasMailbox(PersonColumn.Matt));
        Assert.True(PersonIdentity.HasMailbox(PersonColumn.Ashley));
    }

    [Fact]
    public void TheChildrenDoNot()
    {
        Assert.False(PersonIdentity.HasMailbox(PersonColumn.Gwen));
        Assert.False(PersonIdentity.HasMailbox(PersonColumn.Eva));
    }

    /// <summary>Watch is a column, not a person, so there is nobody there to have an address.</summary>
    [Fact]
    public void TheWatchColumnIsNotAPerson()
    {
        Assert.False(PersonIdentity.HasMailbox(PersonColumn.Watch));
    }

    [Fact]
    public void TheAddressableOnesAreTheParentsInColumnOrder()
    {
        Assert.Equal([PersonColumn.Matt, PersonColumn.Ashley], PersonIdentity.WithMailboxes);
    }
}
