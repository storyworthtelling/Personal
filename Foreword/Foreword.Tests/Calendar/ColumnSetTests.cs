using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// D31: who an event belongs to is a <b>set</b>, and the three sources are read in a fixed order.
///
/// <para>"Gwen and Eva to swimming" is one event in two columns. It used to be one event in Gwen's
/// column, because the family calendar matched the first name it recognised and stopped.</para>
/// </summary>
public class ColumnSetTests
{
    private static readonly IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Addresses =
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [PersonColumn.Matt] = ["matt@example.com"],
            [PersonColumn.Ashley] = ["ashley@example.com"],
            [PersonColumn.Gwen] = ["gwen@example.com"],
            [PersonColumn.Eva] = ["eva@example.com"],
        };

    private static readonly IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Names =
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [PersonColumn.Matt] = ["Matt"],
            [PersonColumn.Ashley] = ["Ashley"],
            [PersonColumn.Gwen] = ["Gwen"],
            [PersonColumn.Eva] = ["Eva"],
        };

    private static ColumnRouting Shared(PersonColumn fallback = PersonColumn.Matt) =>
        new(Column: null, Addresses, Names, Default: fallback);

    private static ColumnRouting MappedTo(PersonColumn column) =>
        new(column, Addresses, Names, Default: PersonColumn.Matt);

    private static CalendarEvent Event(string title, params string[] guests) =>
        new() { Title = title, Guests = guests };

    // ---- source 1: the guests ----

    [Fact]
    public void AnInvitedPersonGetsTheEvent()
    {
        var columns = ColumnSet.Resolve(Event("Swimming", "gwen@example.com"), Shared());

        Assert.Equal([PersonColumn.Gwen], columns);
    }

    [Fact]
    public void TwoInvitedPeopleBothGetIt()
    {
        var columns = ColumnSet.Resolve(
            Event("Swimming", "gwen@example.com", "eva@example.com"), Shared());

        Assert.Equal([PersonColumn.Gwen, PersonColumn.Eva], columns);
    }

    /// <summary>
    /// A guest outside the four is ignored entirely rather than widening the set. Half the invitees
    /// on a real event are other people's parents.
    /// </summary>
    [Fact]
    public void AGuestWhoIsNotOneOfTheFourIsIgnored()
    {
        var columns = ColumnSet.Resolve(
            Event("Swimming", "gwen@example.com", "someone@else.com"), Shared());

        Assert.Equal([PersonColumn.Gwen], columns);
    }

    /// <summary>
    /// The guest list wins over the title. If Eva is invited and the title says Gwen, Eva is going —
    /// somebody typed the title, somebody else sent the invitation, and only one of those is a
    /// record of who is attending.
    /// </summary>
    [Fact]
    public void TheGuestListOutranksTheTitle()
    {
        var columns = ColumnSet.Resolve(Event("Gwen to swimming", "eva@example.com"), Shared());

        Assert.Equal([PersonColumn.Eva], columns);
    }

    /// <summary>
    /// Addresses are matched without regard to case, because Google does not normalise them and a
    /// family address book is typed by hand.
    /// </summary>
    [Fact]
    public void AnAddressMatchesWhateverItsCase()
    {
        var columns = ColumnSet.Resolve(Event("Swimming", "Gwen@Example.COM"), Shared());

        Assert.Equal([PersonColumn.Gwen], columns);
    }

    // ---- source 2: the title ----

    /// <summary>
    /// EVERY name in the title, not the first. This is the bug D31 exists to fix: the family
    /// calendar matched Gwen, returned, and Eva never appeared.
    /// </summary>
    [Fact]
    public void EveryNameInTheTitleCountsNotJustTheFirst()
    {
        var columns = ColumnSet.Resolve(Event("Gwen and Eva to swimming"), Shared());

        Assert.Equal([PersonColumn.Gwen, PersonColumn.Eva], columns);
    }

    [Fact]
    public void ANameInTheTitleIsMatchedWithoutRegardToCase()
    {
        Assert.Equal([PersonColumn.Eva], ColumnSet.Resolve(Event("EVA to swimming"), Shared()));
    }

    // ---- source 3: nobody named on a shared calendar ----

    /// <summary>
    /// Which is what a calendar called Family means. An event nobody is named on is everybody's.
    /// </summary>
    [Fact]
    public void NobodyNamedOnASharedCalendarMeansEverybody()
    {
        var columns = ColumnSet.Resolve(Event("Dinner"), Shared());

        Assert.Equal(ColumnSet.People, columns);
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
            Event("Standup", "ashley@example.com"), MappedTo(PersonColumn.Matt));

        Assert.Equal([PersonColumn.Matt], columns);
    }

    [Fact]
    public void AMappedCalendarIgnoresNamesInTheTitleToo()
    {
        var columns = ColumnSet.Resolve(Event("Call with Gwen"), MappedTo(PersonColumn.Matt));

        Assert.Equal([PersonColumn.Matt], columns);
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
