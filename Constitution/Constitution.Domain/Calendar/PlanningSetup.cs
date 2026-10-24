namespace Constitution.Domain.Calendar;

/// <summary>
/// One adult as the weekly run needs to know them. <paramref name="CalendarId"/> is the id of the calendar the adult's own events
/// are on and their workouts are written to (their primary calendar's id is their address); null or blank means none is set, and then
/// that adult's calendar is unreadable and nothing is placed for them.
/// </summary>
/// <param name="Name">The name as the household uses it: it names the adult in a title, a key and a work schedule.</param>
/// <param name="CalendarId">The calendar's id, or null when none is set.</param>
/// <param name="OnPickup">Whether the adult is on pickup duty; null when not said (taken as yes, and noted as assumed).</param>
/// <param name="Addresses">The adult's email addresses, which a guest list names.</param>
/// <param name="OtherNames">What else the family calls them.</param>
/// <param name="DeliversHere">True on the adult whose pages this phone's tablet receives: each phone is paired to its own tablet.</param>
public sealed record HouseholdAdult(
    string Name,
    string? CalendarId,
    bool? OnPickup = null,
    IReadOnlyList<string>? Addresses = null,
    IReadOnlyList<string>? OtherNames = null,
    bool DeliversHere = false);

/// <summary>
/// What the weekly run needs from the household and cannot work out for itself: who the adults are, which calendars to read, and the
/// few settings the rules take. <b>This is the read side of what the Household group will hold</b> (the roster, C-6.1.4): until that
/// group and the household file exist, a phone reads it from one small file in its own storage (<c>PlanningSetupCodec</c>), and a
/// phone with no such file plans nothing and says so. Nothing in it is a person's data the code could invent, so nothing has a default
/// except the settings with a stated stand-in (<see cref="SetupRules"/>).
///
/// <para>The family and children's calendars are required unless the household says it has none
/// (<see cref="NoFamilyCalendar"/>, <see cref="NoChildren"/>): "not set" and "there is none" are different answers, and only the second may be
/// read as an empty calendar.</para>
/// </summary>
public sealed record PlanningSetup(IReadOnlyList<HouseholdAdult> Adults)
{
    public string? FamilyCalendarId { get; init; }

    public bool NoFamilyCalendar { get; init; }

    public IReadOnlyList<string> ChildrenCalendarIds { get; init; } = [];

    public bool NoChildren { get; init; }

    /// <summary>The household's IANA zone id; null means the phone's own zone is used, and that is noted as assumed.</summary>
    public string? ZoneId { get; init; }

    public TimeOnly? DinnerStart { get; init; }

    public TimeSpan? DinnerLength { get; init; }

    public TimeOnly? Bedtime { get; init; }

    /// <summary>The adult of that name (matched without case), or null.</summary>
    public HouseholdAdult? FindAdult(string name) =>
        Adults.FirstOrDefault(adult => string.Equals(adult.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The calendar id of the adult's own calendar, or null when the adult is unknown or has none set.</summary>
    public string? CalendarOf(string name) =>
        FindAdult(name)?.CalendarId is { } id && !string.IsNullOrWhiteSpace(id) ? id.Trim() : null;

    /// <summary>The adults whose pages this phone delivers.</summary>
    public IReadOnlyList<string> DeliverFor => Adults.Where(adult => adult.DeliversHere).Select(adult => adult.Name).ToList();
}

/// <summary>What reading the household's setup gave: the setup, or the plain reason there is none to use.</summary>
public abstract record HouseholdSetupRead
{
    private HouseholdSetupRead()
    {
    }

    /// <summary>A setup that can be used. It may still lack a calendar; the source says which when it reads.</summary>
    public sealed record Configured(PlanningSetup Setup) : HouseholdSetupRead;

    /// <summary>
    /// No usable setup: nothing was saved, or what was saved could not be read. <paramref name="Reason"/> is a plain sentence that
    /// quotes nothing from the file.
    /// </summary>
    public sealed record NotSet(string Reason) : HouseholdSetupRead;
}
