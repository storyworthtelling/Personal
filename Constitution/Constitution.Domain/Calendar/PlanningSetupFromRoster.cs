using Constitution.Domain.Household;
using Constitution.Domain.Settings;

namespace Constitution.Domain.Calendar;

/// <summary>What the run still lacks. Each is a typed reason the Household group can say; none is filled with a default.</summary>
public enum SetupGap
{
    NoAdults,
    NoAdultDeliversHere,
    DeliversHereIsNotAnAdult,
    FamilyCalendarNotSet,
    ChildrenCalendarsNotSet,
}

/// <summary>The setup built, or null when there is no adult to build it for, with every gap found.</summary>
public sealed record PlanningSetupBuild(PlanningSetup? Setup, IReadOnlyList<SetupGap> Gaps)
{
    /// <summary>True when a file can be written (it may still have gaps, which the run says).</summary>
    public bool CanWrite => Setup is not null;

    public bool IsComplete => Setup is not null && Gaps.Count == 0;
}

/// <summary>
/// Makes the <see cref="PlanningSetup"/> the weekly run reads, from the roster and the owner's choices. <b>Nothing is defaulted</b>:
/// a bedtime, dinner or zone not chosen stays null (the run takes its stated stand-in and says so, <see cref="SetupRules"/>), and a
/// missing calendar or delivering adult comes back as a typed <see cref="SetupGap"/> rather than a guess.
/// </summary>
public static class PlanningSetupFromRoster
{
    public static PlanningSetupBuild Build(RosterView roster, HouseholdChoices choices)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(choices);

        var adults = roster.Adults
            .Select(entry => new HouseholdAdult(
                entry.Name,
                CalendarOf(choices, entry.Name),
                entry.OnPickup,
                entry.GoogleAddress is { } address ? [address] : null,
                null,
                Delivers(choices, entry.Name)))
            .ToList();
        return Compose(adults, roster.Children.Count == 0, choices);
    }

    /// <summary>With no roster to read, the adults a saved setup already names take the new choices.</summary>
    public static PlanningSetupBuild Apply(PlanningSetup existing, HouseholdChoices choices)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(choices);

        var adults = existing.Adults
            .Select(adult => adult with
            {
                CalendarId = choices.AdultCalendars.Any(pair => Same(pair.Key, adult.Name)) ? CalendarOf(choices, adult.Name) : adult.CalendarId,
                OnPickup = choices.Pickup.Where(pair => Same(pair.Key, adult.Name)).Select(pair => (bool?)pair.Value).FirstOrDefault() ?? adult.OnPickup,
                DeliversHere = Delivers(choices, adult.Name),
            })
            .ToList();
        return Compose(adults, choices.NoChildren, choices);
    }

    private static PlanningSetupBuild Compose(List<HouseholdAdult> adults, bool noChildren, HouseholdChoices choices)
    {
        if (adults.Count == 0)
        {
            return new PlanningSetupBuild(null, [SetupGap.NoAdults]);
        }

        var children = choices.ChildrenCalendarIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();
        var family = string.IsNullOrWhiteSpace(choices.FamilyCalendarId) ? null : choices.FamilyCalendarId.Trim();
        var setup = new PlanningSetup(adults)
        {
            FamilyCalendarId = family,
            NoFamilyCalendar = choices.NoFamilyCalendar,
            ChildrenCalendarIds = children,
            NoChildren = noChildren,
            ZoneId = string.IsNullOrWhiteSpace(choices.ZoneId) ? null : choices.ZoneId.Trim(),
            DinnerStart = choices.DinnerStart,
            DinnerLength = choices.DinnerLength,
            Bedtime = choices.Bedtime,
        };

        var gaps = new List<SetupGap>();
        if (string.IsNullOrWhiteSpace(choices.DeliversHere))
        {
            gaps.Add(SetupGap.NoAdultDeliversHere);
        }
        else if (!adults.Any(adult => adult.DeliversHere))
        {
            gaps.Add(SetupGap.DeliversHereIsNotAnAdult);
        }

        if (family is null && !choices.NoFamilyCalendar)
        {
            gaps.Add(SetupGap.FamilyCalendarNotSet);
        }

        if (!noChildren && children.Count == 0)
        {
            gaps.Add(SetupGap.ChildrenCalendarsNotSet);
        }

        return new PlanningSetupBuild(setup, gaps);
    }

    private static string? CalendarOf(HouseholdChoices choices, string name) =>
        choices.AdultCalendars.Where(pair => Same(pair.Key, name) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Value.Trim()).FirstOrDefault();

    private static bool Delivers(HouseholdChoices choices, string name) =>
        !string.IsNullOrWhiteSpace(choices.DeliversHere) && Same(choices.DeliversHere, name);

    private static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
