using System.Reflection;
using System.Text.RegularExpressions;
using Constitution.Domain.Placement;

namespace Constitution.Tests.Placement;

/// <summary>
/// The shape of the calendar seam, held as a test so widening it is a decision rather than a drift. The
/// planner may look an event up by key and may create one; it may not move, change or remove one, because
/// "the owner's edits win" is only true if the planner has no way to say otherwise.
/// </summary>
public class PlacementSeamTests
{
    [Fact]
    public void TheWriterCanOnlyFindAndCreate()
    {
        var methods = typeof(ICalendarWriter).GetMethods().Select(method => method.Name).Order().ToList();

        Assert.Equal(["CreateAsync", "FindAsync"], methods);
    }

    [Fact]
    public void TheWriterNamesAPersonNotACalendar()
    {
        // Writes go to a person's primary calendar. There is nowhere in the seam to name another one.
        var parameters = typeof(ICalendarWriter).GetMethods()
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name!)
            .ToList();

        Assert.DoesNotContain(parameters, name => name.Contains("calendar", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("person", parameters);
    }

    [Fact]
    public void WritesUseTheFullCalendarScope()
    {
        // The head's sign-in asks for the full scope. Pulp's narrower calendar.app.created covers only a
        // calendar the app made, and these events go on the owner's own primary one.
        var file = ConstitutionSource.Files(".cs").Single(path => path.EndsWith("Services/ConstitutionGoogle.cs", StringComparison.Ordinal));
        var text = File.ReadAllText(file);

        Assert.Matches("\"https://www\\.googleapis\\.com/auth/calendar\"", text);
        Assert.DoesNotContain("calendar.app.created", text, StringComparison.Ordinal);
        Assert.DoesNotContain("calendar.events", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEventsLeaveNoOneInvited()
    {
        // An invitation would announce the workout to someone; the events carry no guests at all.
        var created = typeof(EventToCreate).GetProperties().Select(property => property.Name).ToList();

        Assert.DoesNotContain(created, name => Regex.IsMatch(name, "Guest|Attendee|Invite|Notify", RegexOptions.CultureInvariant));
    }
}
