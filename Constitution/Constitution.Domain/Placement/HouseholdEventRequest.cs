namespace Constitution.Domain.Placement;

/// <summary>A stretch of time an event may be placed in. It has to run forward.</summary>
public sealed record PlacementWindow
{
    public PlacementWindow(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentException("A placement window has to end after it starts.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }
}

/// <summary>
/// Something the household does that takes the time of one or more adults and is not a workout: cooking
/// dinner, a shop for groceries, starting the slow cooker. This is the whole seam for Release D's meal
/// planner; nothing here knows what a meal is. The caller says <i>what</i>, <i>how long</i>, <i>whose time</i>
/// and <i>where it may go</i> (windows, in order of preference); placement finds the earliest minute inside the
/// first window that has room for everyone, and never on busy or never-placed time.
///
/// <para>The event is written to the primary calendar of <b>every</b> person named, as one event each, so
/// "who" is also "whose calendar".  <see cref="Identity"/> is the caller's own name for the event within its
/// week and person (<c>dinner-a</c>, <c>shop-a</c>); it goes into the key, so it must be stable from run to run
/// and must not be, or hold, a date (<see cref="PlacementKey.ForHousehold"/> refuses one).</para>
/// </summary>
public sealed record HouseholdEventRequest
{
    public HouseholdEventRequest(
        string identity,
        string title,
        TimeSpan length,
        IReadOnlyList<string> people,
        IReadOnlyList<PlacementWindow> windows)
    {
        PlacementKey.CheckIdentity(identity);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A household event needs a title.", nameof(title));
        }

        if (length <= TimeSpan.Zero)
        {
            throw new ArgumentException("A household event takes some time.", nameof(length));
        }

        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(windows);
        if (people.Count == 0 || people.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A household event is for at least one named person.", nameof(people));
        }

        var names = people.Select(person => person.Trim()).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            throw new ArgumentException("A household event names the same person twice.", nameof(people));
        }

        if (windows.Count == 0)
        {
            throw new ArgumentException("A household event needs at least one window to go in.", nameof(windows));
        }

        Identity = identity.Trim();
        Title = title.Trim();
        Length = length;
        People = names.AsReadOnly();
        Windows = windows.ToList().AsReadOnly();
    }

    public string Identity { get; }

    /// <summary>The title as written on the calendar, trimmed. It is used as given and may not contain the key.</summary>
    public string Title { get; }

    public TimeSpan Length { get; }

    /// <summary>The adults whose time it takes, and whose primary calendars it is written to.</summary>
    public IReadOnlyList<string> People { get; }

    /// <summary>Where it may go, in order of preference.</summary>
    public IReadOnlyList<PlacementWindow> Windows { get; }
}
