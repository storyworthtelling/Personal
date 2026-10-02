using Foreword.Domain.Calendar;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Calendar;

/// <summary>
/// D34: an unmapped calendar prints nothing and <b>says so</b>. Never silently printed, never
/// silently dropped.
///
/// <para>Both silent outcomes are bad in their own way and the old behaviour was the first: an
/// unmapped calendar fell to the default column, so a new calendar the owner subscribed to —
/// somebody's sports fixtures, a school newsletter feed — started printing into Alex's column
/// without anything having asked. Dropping it instead would be worse: the owner would be looking
/// for something that had never arrived, with nothing anywhere to say why.</para>
///
/// <para>So it is <b>held</b>: its events are kept out of the page, and the calendar is named, so
/// the answer to "where is my thing" is on the screen rather than in a log.</para>
/// </summary>
public class HeldCalendarsTests
{
    private static CalendarEvent From(string calendarId, string title = "Thing") =>
        new() { Title = title, SourceCalendarId = calendarId };

    private static readonly IReadOnlySet<string> Known =
        new HashSet<string> { "alex@work", "family@group" };

    [Fact]
    public void AKnownCalendarIsNotHeld()
    {
        var split = HeldCalendars.Split([From("alex@work")], Known);

        Assert.Single(split.Printed);
        Assert.Empty(split.Held);
    }

    [Fact]
    public void AnUnknownCalendarIsHeldRatherThanPrinted()
    {
        var split = HeldCalendars.Split([From("scouts@group")], Known);

        Assert.Empty(split.Printed);
        Assert.Equal(["scouts@group"], split.Held);
    }

    /// <summary>
    /// The events of a held calendar are kept out of the page — the whole point of holding it. An
    /// unmapped calendar that still printed would be the old bug wearing a warning label.
    /// </summary>
    [Fact]
    public void TheEventsOfAHeldCalendarDoNotReachThePage()
    {
        var split = HeldCalendars.Split(
            [From("alex@work", "Standup"), From("scouts@group", "Pack meeting")], Known);

        Assert.Equal(["Standup"], split.Printed.Select(e => e.Title));
    }

    /// <summary>
    /// A calendar is named once however many events it brought. The owner needs to know which
    /// calendar to map, not how busy it is.
    /// </summary>
    [Fact]
    public void ACalendarIsNamedOnceHoweverManyEventsItBrought()
    {
        var split = HeldCalendars.Split(
            [From("scouts@group", "A"), From("scouts@group", "B"), From("scouts@group", "C")], Known);

        Assert.Equal(["scouts@group"], split.Held);
    }

    [Fact]
    public void SeveralUnknownCalendarsAreAllNamed()
    {
        var split = HeldCalendars.Split([From("scouts@group"), From("school@group")], Known);

        Assert.Equal(2, split.Held.Count);
        Assert.Contains("scouts@group", split.Held);
        Assert.Contains("school@group", split.Held);
    }

    /// <summary>
    /// An event with no source calendar at all is printed rather than held. It did not come from a
    /// calendar the owner could map — a note read back off the paper is the case — so holding it
    /// would be asking them to fix something that is not broken.
    /// </summary>
    [Fact]
    public void AnEventWithNoSourceCalendarIsPrinted()
    {
        var split = HeldCalendars.Split([From(string.Empty, "Written on the page")], Known);

        Assert.Single(split.Printed);
        Assert.Empty(split.Held);
    }

    [Fact]
    public void NothingIsHeldWhenEveryCalendarIsKnown()
    {
        var split = HeldCalendars.Split([From("alex@work"), From("family@group")], Known);

        Assert.Equal(2, split.Printed.Count);
        Assert.Empty(split.Held);
    }
}
