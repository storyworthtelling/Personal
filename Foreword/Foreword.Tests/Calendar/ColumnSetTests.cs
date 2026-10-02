using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// D31: who an event belongs to is a <b>set</b>, and the three sources are read in a fixed order.
///
/// <para>"Riley and Jo to swimming" is one event in two columns. It used to be one event in Riley's
/// column, because the family calendar matched the first name it recognised and stopped.</para>
/// </summary>
public class ColumnSetTests
{
    private static readonly IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Addresses =
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [Household.Alex] = ["alex@example.com"],
            [Household.Sam] = ["sam@example.com"],
            [Household.Riley] = ["riley@example.com"],
            [Household.Jo] = ["jo@example.com"],
        };

    private static readonly IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Names =
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [Household.Alex] = ["Alex"],
            [Household.Sam] = ["Sam"],
            [Household.Riley] = ["Riley"],
            [Household.Jo] = ["Jo"],
        };

    private static ColumnRouting Shared(PersonColumn? fallback = null) =>
        new(Household.Layout.People, Column: null, Addresses, Names, Default: fallback ?? Household.Alex);

    private static ColumnRouting MappedTo(PersonColumn column) =>
        new(Household.Layout.People, column, Addresses, Names, Default: Household.Alex);

    private static CalendarEvent Event(string title, params string[] guests) =>
        new() { Title = title, Guests = guests };

    // ---- source 1: the guests ----

    [Fact]
    public void AnInvitedPersonGetsTheEvent()
    {
        var columns = ColumnSet.Resolve(Event("Swimming", "riley@example.com"), Shared());

        Assert.Equal([Household.Riley], columns);
    }

    [Fact]
    public void TwoInvitedPeopleBothGetIt()
    {
        var columns = ColumnSet.Resolve(
            Event("Swimming", "riley@example.com", "jo@example.com"), Shared());

        Assert.Equal([Household.Riley, Household.Jo], columns);
    }

    /// <summary>
    /// A guest outside the four is ignored entirely rather than widening the set. Half the invitees
    /// on a real event are other people's parents.
    /// </summary>
    [Fact]
    public void AGuestWhoIsNotOneOfTheFourIsIgnored()
    {
        var columns = ColumnSet.Resolve(
            Event("Swimming", "riley@example.com", "someone@else.com"), Shared());

        Assert.Equal([Household.Riley], columns);
    }

    /// <summary>
    /// The guest list wins over the title. If Jo is invited and the title says Riley, Jo is going —
    /// somebody typed the title, somebody else sent the invitation, and only one of those is a
    /// record of who is attending.
    /// </summary>
    [Fact]
    public void TheGuestListOutranksTheTitle()
    {
        var columns = ColumnSet.Resolve(Event("Riley to swimming", "jo@example.com"), Shared());

        Assert.Equal([Household.Jo], columns);
    }

    /// <summary>
    /// Addresses are matched without regard to case, because Google does not normalise them and a
    /// family address book is typed by hand.
    /// </summary>
    [Fact]
    public void AnAddressMatchesWhateverItsCase()
    {
        var columns = ColumnSet.Resolve(Event("Swimming", "Riley@Example.COM"), Shared());

        Assert.Equal([Household.Riley], columns);
    }

    // ---- source 2: the title ----

    /// <summary>
    /// EVERY name in the title, not the first. This is the bug D31 exists to fix: the family
    /// calendar matched Riley, returned, and Jo never appeared.
    /// </summary>
    [Fact]
    public void EveryNameInTheTitleCountsNotJustTheFirst()
    {
        var columns = ColumnSet.Resolve(Event("Riley and Jo to swimming"), Shared());

        Assert.Equal([Household.Riley, Household.Jo], columns);
    }

    [Fact]
    public void ANameInTheTitleIsMatchedWithoutRegardToCase()
    {
        Assert.Equal([Household.Jo], ColumnSet.Resolve(Event("JO to swimming"), Shared()));
    }

    // ---- source 3: nobody named on a shared calendar ----

    /// <summary>
    /// Which is what a calendar called Family means. An event nobody is named on is everybody's.
    /// </summary>
    [Fact]
    public void NobodyNamedOnASharedCalendarMeansEverybody()
    {
        var columns = ColumnSet.Resolve(Event("Dinner"), Shared());

        Assert.Equal(Household.Layout.People, columns);
    }

    // ---- a calendar that belongs to one person ----

    /// <summary>
    /// A calendar mapped to one person needs no splitting: its events are that person's, whoever
    /// else is invited. Reading guests here would move a work meeting out of the work calendar's
    /// column the moment a spouse was copied in.
    /// </summary>
    [Fact]
    public void AMappedCalendarKeepsItsOwnColumnWhoeverIsInvited()
    {
        var columns = ColumnSet.Resolve(
            Event("Standup", "sam@example.com"), MappedTo(Household.Alex));

        Assert.Equal([Household.Alex], columns);
    }

    [Fact]
    public void AMappedCalendarIgnoresNamesInTheTitleToo()
    {
        var columns = ColumnSet.Resolve(Event("Call with Riley"), MappedTo(Household.Alex));

        Assert.Equal([Household.Alex], columns);
    }

    // ---- the set is never empty ----

    /// <summary>
    /// Every event lands somewhere. An empty set would be an event that exists in the calendar and
    /// on no page, which is the one outcome worse than being in the wrong column.
    /// </summary>
    [Theory]
    [InlineData("Dentist")]
    [InlineData("")]
    [InlineData("A meeting with nobody in particular")]
    public void TheSetIsNeverEmpty(string title)
    {
        Assert.NotEmpty(ColumnSet.Resolve(Event(title), Shared()));
    }
}
