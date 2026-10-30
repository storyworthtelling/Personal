using System.Globalization;
using Constitution.Domain.Groceries;

namespace Constitution.Domain.Placement;

/// <summary>
/// Which run wrote an event, told from its key alone. Every placement run drops its own earlier events when it reads the calendars (read back as busy
/// time, its week would never settle) and must keep everyone else's: the other run's events are cook blocks and workouts that sit on somebody's
/// day, and a run that cannot see them places on top of them.
/// </summary>
public enum KeyFamily
{
    /// <summary>
    /// A key this app wrote but no run owns yet, or text that is not a key at all. Never a family, so never dropped: unknown means somebody's time.
    /// </summary>
    Unknown = 0,

    /// <summary>A workout (<see cref="PlacementKey.ForWorkout"/>): written by the weekly workout run, or re-keyed by the Workouts tab.</summary>
    Workout = 1,

    /// <summary>A household meal event (<see cref="PlacementKey.ForHousehold"/>): a cook block, a slow-cooker start or the Groceries trip, written by the weekly meal run.</summary>
    HouseholdMeal = 2,
}

/// <summary>
/// The family a stored key belongs to. Pure. Deliberately strict: a household key is a meal event only when its identity is one of the three words
/// the meal run writes, so a future kind of household event is <see cref="KeyFamily.Unknown"/> (kept as busy by both runs) until someone gives it a
/// family on purpose.
///
/// <para><b>The words are repeated here, frozen, and pinned.</b> The cook and slow-cooker words are private to <c>MealPlacement</c> and the household
/// kind word to <see cref="PlacementKey"/>, so this file spells them again and <c>KeyFamilyTests</c> derives a key for every role on every evening
/// through those types and requires this file to place each in its family: a word that moved over there turns that test red, not a run's week quiet.
/// A workout key is told by <c>PlacementKey</c> itself (its frozen kind names), so a new session kind is a family member the day it has a name.</para>
/// </summary>
public static class KeyFamilies
{
    private const char Separator = ':';
    private const int Parts = 5;
    private const string HouseholdKind = "household";
    private const string CookWord = "cook";
    private const string SlowCookerWord = "slowcooker";

    // Indexed by DayOfWeek's own numbering, the spelling MealPlacement writes into a key. Never the date.
    private static readonly string[] DayWords = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>The family of the text an event's private property held. Null, blank or anything this app did not write is <see cref="KeyFamily.Unknown"/>.</summary>
    public static KeyFamily Of(string? stored) =>
        PlacementKey.TryFromStored(stored, out var key) ? Of(key) : KeyFamily.Unknown;

    /// <summary>The family of a key. A default key is <see cref="KeyFamily.Unknown"/>.</summary>
    public static KeyFamily Of(PlacementKey key)
    {
        if (PlacementKey.TryWorkoutParts(key, out _, out _, out _, out _))
        {
            return KeyFamily.Workout;
        }

        var parts = key.Value.Split(Separator);
        if (parts.Length != Parts
            || parts[2] != HouseholdKind
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return KeyFamily.Unknown;
        }

        return IsMealIdentity(parts[4]) ? KeyFamily.HouseholdMeal : KeyFamily.Unknown;
    }

    private static bool IsMealIdentity(string identity)
    {
        if (identity == GroceryPlacement.Identity)
        {
            return true;
        }

        foreach (var role in new[] { CookWord, SlowCookerWord })
        {
            if (identity.StartsWith(role + "-", StringComparison.Ordinal) && DayWords.Contains(identity[(role.Length + 1)..], StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
