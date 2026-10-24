using Constitution.Domain.Workouts;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The word the workout files use for a kind of session: the delivered-pages file and the owner's-moves file. Each is written out here
/// as a literal and read back by exact match.
///
/// <para>It was <c>Enum.ToString()</c> and <c>Enum.Parse</c>, which ties what a file on a phone says to what a member is called in the
/// source (a rename would quietly rewrite the word, and every file already written would stop reading) and which reads a number, a list or
/// a differently spelled word back as a member (<c>"3"</c> parsed as Mobility). The words below are exactly what those calls produced, so
/// files already written still read; <c>WorkoutWireNamesTests</c> holds a second copy of them on purpose, so that moving one fails there.
/// A new kind is a deliberate edit to both, and a kind with no word here cannot be written.</para>
/// </summary>
public static class WorkoutWireNames
{
    public static string Of(SessionKind kind) => kind switch
    {
        SessionKind.Strength => "Strength",
        SessionKind.OutdoorCardio => "OutdoorCardio",
        SessionKind.Treadmill => "Treadmill",
        SessionKind.Mobility => "Mobility",
        SessionKind.GentleWalk => "GentleWalk",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This kind of session has no word in the workout files. Give it one on purpose."),
    };

    /// <summary>The kind a word names, or null for any word this app never wrote.</summary>
    public static SessionKind? SessionKindOf(string? word) => word switch
    {
        "Strength" => SessionKind.Strength,
        "OutdoorCardio" => SessionKind.OutdoorCardio,
        "Treadmill" => SessionKind.Treadmill,
        "Mobility" => SessionKind.Mobility,
        "GentleWalk" => SessionKind.GentleWalk,
        _ => null,
    };
}
