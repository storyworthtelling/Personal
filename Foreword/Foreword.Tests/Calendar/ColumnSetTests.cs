using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// D31: who an event belongs to is a <b>set</b>, and the three sources are read in a fixed order.
///
/// <para>"Wren and Linden to swimming" is one event in two columns. It used to be one event in Wren's
/// column, because the family calendar matched the first name it recognised and stopped.</para>
/// </summary>
public class ColumnSetTests
{
    private static readonly IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Addresses =
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [PersonColumn.Rowan] = ["rowan@example.com"],
            [PersonColumn.Jordan] = ["jordan@example.com"],
            [PersonColumn.Wren] = ["wren@example.com"],
            [PersonColumn.Linden] = ["linden@example.com"],
        };

    private static readonly IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Names =
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [PersonColumn.Rowan] = ["Rowan"],
            [PersonColumn.Jordan] = ["Jordan"],
            [PersonColumn.Wren] = ["Wren"],
            [PersonColumn.Linden] = ["Linden"],
        };

    private static ColumnRouting Shared(PersonColumn fallback = PersonColumn.Rowan) =>
        new(Column: null, Addresses, Names, Default: fallback);

    private static ColumnRouting MappedTo(PersonColumn column) =>
        new(column, Addresses, Names, Default: PersonColumn.Rowan);

    private static CalendarEvent Event(string title, params string[] guests) =>
        new() { Title = title, Guests = guests };

    // ---- source 1: the guests ----

    [Fact]
    public void AnInvitedPersonGetsTheEvent()
    {
        var columns = ColumnSet.Resolve(Event("Swimming", "wren@example.com"), Shared());

        Assert.Equal([PersonColumn.Wren], columns);
    }

    [Fact]
    public void TwoInvitedPeopleBothGetIt()
    {
        var columns = ColumnSet.Resolve(
            Event("Swimming", "wren@example.com", "linden@example.com"), Shared());

        Assert.Equal([PersonColumn.Wren, PersonColumn.Linden], columns);
    }

    /// <summary>
    /// A guest outside the four is ignored entirely rather than widening the set. Half the invitees
    /// on a real event are other people's parents.
    /// </summary>
    [Fact]
    public void AGuestWhoIsNotOneOfTheFourIsIgnored()
    {
        var columns = ColumnSet.Resolve(
            Event("Swimming", "wren@example.com", "someone@else.com"), Shared());

        Assert.Equal([PersonColumn.Wren], columns);
    }

    /// <summary>
    /// The guest list wins over the title. If Linden is invited and the title says Wren, Linden is going —
    /// somebody typed the title, somebody else sent the invitation, and only one of those is a
    /// record of who is attending.
    /// </summary>
    [Fact]
    public void TheGuestListOutranksTheTitle()
    {
        var columns = ColumnSet.Resolve(Event("Wren to swimming", "linden@example.com"), Shared());

        Assert.Equal([PersonColumn.Linden], columns);
    }

    /// <summary>
    /// Addresses are matched without regard to case, because Google does not normalise them and a
    /// family address book is typed by hand.
    /// </summary>
    [Fact]
    public void AnAddressMatchesWhateverItsCase()
    {
        var columns = ColumnSet.Resolve(Event("Swimming", "Wren@Example.COM"), Shared());

        Assert.Equal([PersonColumn.Wren], columns);
    }

    // ---- source 2: the title ----

    /// <summary>
    /// EVERY name in the title, not the first. This is the bug D31 exists to fix: the family
    /// calendar matched Wren, returned, and Linden never appeared.
    /// </summary>
    [Fact]
    public void EveryNameInTheTitleCountsNotJustTheFirst()
    {
        var columns = ColumnSet.Resolve(Event("Wren and Linden to swimming"), Shared());

        Assert.Equal([PersonColumn.Wren, PersonColumn.Linden], columns);
    }

    [Fact]
    public void ANameInTheTitleIsMatchedWithoutRegardToCase()
    {
        Assert.Equal([PersonColumn.Linden], ColumnSet.Resolve(Event("LINDEN to swimming"), Shared()));
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
            Event("Standup", "jordan@example.com"), MappedTo(PersonColumn.Rowan));

        Assert.Equal([PersonColumn.Rowan], columns);
    }

    [Fact]
    public void AMappedCalendarIgnoresNamesInTheTitleToo()
    {
        var columns = ColumnSet.Resolve(Event("Call with Wren"), MappedTo(PersonColumn.Rowan));

        Assert.Equal([PersonColumn.Rowan], columns);
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
