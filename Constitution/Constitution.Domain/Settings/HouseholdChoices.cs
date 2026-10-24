using Constitution.Domain.Calendar;

namespace Constitution.Domain.Settings;

/// <summary>
/// What the Household group lets the owner choose on this phone, beyond who the people are: the children's bedtime, the family dinner,
/// the calendars, who is on pickup when no roster says, and which adult this phone's tablet belongs to. Every value is nullable or
/// empty by design: "not chosen" stays visible as assumed rather than turning into a default.
/// </summary>
public sealed record HouseholdChoices
{
    public TimeOnly? Bedtime { get; init; }

    public TimeOnly? DinnerStart { get; init; }

    public TimeSpan? DinnerLength { get; init; }

    public string? ZoneId { get; init; }

    public string? FamilyCalendarId { get; init; }

    public bool NoFamilyCalendar { get; init; }

    public IReadOnlyList<string> ChildrenCalendarIds { get; init; } = [];

    public bool NoChildren { get; init; }

    /// <summary>The name of the adult whose pages this phone delivers; null when none is chosen.</summary>
    public string? DeliversHere { get; init; }

    /// <summary>Each adult's own calendar id, keyed by name (matched without case).</summary>
    public IReadOnlyDictionary<string, string> AdultCalendars { get; init; } = new Dictionary<string, string>();

    /// <summary>Pickup, keyed by adult name; used only where no roster supplies it.</summary>
    public IReadOnlyDictionary<string, bool> Pickup { get; init; } = new Dictionary<string, bool>();

    /// <summary>The choices a saved setup already holds.</summary>
    public static HouseholdChoices From(PlanningSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var calendars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pickup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var adult in setup.Adults)
        {
            if (setup.CalendarOf(adult.Name) is { } calendar)
            {
                calendars[adult.Name] = calendar;
            }

            if (adult.OnPickup is { } onPickup)
            {
                pickup[adult.Name] = onPickup;
            }
        }

        return new HouseholdChoices
        {
            Bedtime = setup.Bedtime,
            DinnerStart = setup.DinnerStart,
            DinnerLength = setup.DinnerLength,
            ZoneId = setup.ZoneId,
            FamilyCalendarId = setup.FamilyCalendarId,
            NoFamilyCalendar = setup.NoFamilyCalendar,
            ChildrenCalendarIds = setup.ChildrenCalendarIds,
            NoChildren = setup.NoChildren,
            DeliversHere = setup.DeliverFor.FirstOrDefault(),
            AdultCalendars = calendars,
            Pickup = pickup,
        };
    }
}
