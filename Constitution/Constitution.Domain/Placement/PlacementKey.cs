using System.Globalization;
using System.Text.RegularExpressions;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Placement;

/// <summary>
/// The hidden key an event carries in its private extended properties (<see cref="Property"/>) so the next
/// run can find the event again wherever the owner has moved it. <b>It never includes the date</b>: if it
/// did, a moved event would be a different event, the old one would look deleted and a new one would be made.
/// And it never appears in a title (<see cref="PlacementEngine"/> refuses a title that holds its own key).
///
/// <para><b>What names an event.</b> Who it is on the calendar of, what kind of thing it is, which week's plan
/// it came from, and a slot in that week: for a workout, the number issued to the session
/// (<see cref="KeyAssignments"/>, <see cref="SessionKeys"/>), which for a week planned once is the count of sessions
/// of that kind before it (so a second strength session is not the first) and which, once issued, stays with that
/// session however the week is later re-planned; for a household event, the identity its caller chose. The week
/// is a number (<see cref="WeekIndexOf"/>), not a day, and nothing about where in the week the event sits goes in
/// at all.</para>
///
/// <para><b>The spelling is frozen</b>, as a literal under <c>PlacementKeyTests</c>: it is stored on events in
/// the owner's Google Calendar, so changing it strands every event already written. Treat the text as opaque:
/// compare keys, never parse them. Enum names are never used to build one, since renaming a
/// <see cref="SessionKind"/> would otherwise rename the key (<c>EverySessionKindHasAFrozenName</c>).</para>
/// </summary>
public readonly record struct PlacementKey
{
    /// <summary>The name of the private extended property the key is stored under. Frozen.</summary>
    public const string Property = "constitutionKey";

    private const string Version = "c1";
    private const char Separator = ':';
    private const int Parts = 5;
    private const int DaysPerWeek = 7;
    private const string HouseholdKind = "household";

    // Shapes a date takes: 2026-11-03, 2026/11/3, 11/03/2026, 3.11.2026, and any run of six or more digits
    // (20261103, 261103). A caller's identity may hold none of them.
    private static readonly Regex DateShapes = new(
        @"\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4}|\d{6,}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly string? text;

    private PlacementKey(string text)
    {
        this.text = text;
    }

    /// <summary>The key as stored. Empty for a default key, never null.</summary>
    public string Value => text ?? string.Empty;

    /// <summary>
    /// The number of the week the date falls in, counting Monday-to-Sunday weeks (the same arithmetic the
    /// workout planner uses). Every day of a week gives the same number.
    /// </summary>
    public static int WeekIndexOf(DateOnly weekStart) => weekStart.DayNumber / DaysPerWeek;

    /// <summary>The key of a workout: <paramref name="ordinal"/> is the number issued to this person's session of this kind in the week (see <see cref="SessionKeys"/>).</summary>
    /// <exception cref="ArgumentException">The person is blank, the kind has no frozen name, or a number is negative.</exception>
    public static PlacementKey ForWorkout(string person, SessionKind kind, int weekIndex, int ordinal)
    {
        CheckNumber(weekIndex, nameof(weekIndex));
        CheckNumber(ordinal, nameof(ordinal));
        return new PlacementKey(Compose(person, KindName(kind), weekIndex, ordinal.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The key of a household event, by the identity its caller gave it (see <see cref="CheckIdentity"/>).</summary>
    /// <exception cref="ArgumentException">The person or identity is blank, or the identity holds a date.</exception>
    public static PlacementKey ForHousehold(string person, int weekIndex, string identity)
    {
        CheckNumber(weekIndex, nameof(weekIndex));
        CheckIdentity(identity);
        return new PlacementKey(Compose(person, HouseholdKind, weekIndex, Escape(identity)));
    }

    /// <summary>A key read back from an event's extended property.</summary>
    /// <exception cref="ArgumentException">The text is not a key this app wrote.</exception>
    public static PlacementKey FromStored(string stored)
    {
        if (TryFromStored(stored, out var key))
        {
            return key;
        }

        throw new ArgumentException("That text is not a placement key this app wrote.", nameof(stored));
    }

    /// <summary>As <see cref="FromStored"/>, for a caller that expects to meet text which is not ours.</summary>
    public static bool TryFromStored(string? stored, out PlacementKey key)
    {
        key = default;
        var trimmed = stored?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var parts = trimmed.Split(Separator);
        if (parts.Length != Parts || parts[0] != Version || parts.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        key = new PlacementKey(trimmed);
        return true;
    }

    /// <summary>
    /// A household event's identity must be something, and must not be a date: dates move, and the key must not.
    /// Six or more digits in a row count as a date, so a caller needs a short identity, not an id from outside.
    /// </summary>
    /// <exception cref="ArgumentException">The identity is blank or holds a date.</exception>
    internal static void CheckIdentity(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException("A household event needs an identity that names it from week to week.", nameof(identity));
        }

        if (DateShapes.IsMatch(identity))
        {
            throw new ArgumentException(
                $"The identity '{identity}' looks like a date. A key never includes the date, so a moved event can be found again.",
                nameof(identity));
        }
    }

    public override string ToString() => Value;

    private static string Compose(string person, string kind, int weekIndex, string slot)
    {
        if (string.IsNullOrWhiteSpace(person))
        {
            throw new ArgumentException("A key names the person whose calendar the event is on.", nameof(person));
        }

        return string.Join(Separator, Version, Escape(person), kind, weekIndex.ToString(CultureInfo.InvariantCulture), slot);
    }

    // Percent-encoding makes the separator unsayable inside a part, so no name can forge another key.
    private static string Escape(string part) => Uri.EscapeDataString(part.Trim().ToLowerInvariant());

    private static void CheckNumber(int value, string name)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "A week or a slot is counted from zero.");
        }
    }

    /// <summary>
    /// What a workout key says: whose, which kind, which week, which number. False for a household key or text that is not a key.
    /// The one place a key is taken apart, and it is the key's own file: callers elsewhere compare keys and never parse them.
    /// </summary>
    internal static bool TryWorkoutParts(PlacementKey key, out string person, out SessionKind kind, out int week, out int ordinal)
    {
        person = string.Empty;
        kind = default;
        week = 0;
        ordinal = 0;
        var parts = key.Value.Split(Separator);
        if (parts.Length != Parts || parts[0] != Version || !TryKindNamed(parts[2], out kind))
        {
            return false;
        }

        if (!int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out week)
            || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out ordinal))
        {
            return false;
        }

        person = Uri.UnescapeDataString(parts[1]);
        return true;
    }

    /// <summary>The frozen spelling of a kind inside a key, for the one other record that stores it (<see cref="KeyAssignmentsCodec"/>).</summary>
    internal static string NameOfKind(SessionKind kind) => KindName(kind);

    /// <summary>The kind a frozen spelling names, or false for a spelling this app never wrote.</summary>
    internal static bool TryKindNamed(string? name, out SessionKind kind)
    {
        foreach (var candidate in Enum.GetValues<SessionKind>())
        {
            if (string.Equals(KindName(candidate), name, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    private static string KindName(SessionKind kind) => kind switch
    {
        SessionKind.Strength => "strength",
        SessionKind.OutdoorCardio => "outdoorcardio",
        SessionKind.Treadmill => "treadmill",
        SessionKind.Mobility => "mobility",
        SessionKind.GentleWalk => "gentlewalk",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This kind of session has no frozen name in the calendar key. Give it one on purpose."),
    };
}
