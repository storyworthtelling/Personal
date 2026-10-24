using Constitution.Domain.Delivery;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Documents;
using Constitution.Domain.Workouts;

namespace Constitution.Infrastructure.ReadBack;

/// <summary>
/// The words the read-back's files use for the closed lists they hold: the kind of document, of session and of box, and
/// the outcome the status file keeps. Each is written out here as a literal and read back by exact match.
///
/// <para>They were <c>Enum.ToString()</c> and <c>Enum.Parse</c>, which ties what a file on a phone says to what a member is
/// called in the source (a rename would quietly rewrite the word, and every file already written would stop reading), and
/// which reads back a number, a list or a differently-cased word as a member. The words below are what those calls
/// produced, so files already written still read; <c>ReadBackWireNamesTests</c> holds a second copy of them on purpose, so
/// that moving one fails there. A new member is a deliberate edit to both.</para>
/// </summary>
public static class ReadBackWireNames
{
    public static string Of(DeliveryKind kind) => kind switch
    {
        DeliveryKind.Workout => "Workout",
        DeliveryKind.Meals => "Meals",
        DeliveryKind.Digest => "Digest",
        _ => throw Unnamed(kind),
    };

    public static DeliveryKind? DeliveryKindOf(string? name) => name switch
    {
        "Workout" => DeliveryKind.Workout,
        "Meals" => DeliveryKind.Meals,
        "Digest" => DeliveryKind.Digest,
        _ => null,
    };

    public static string Of(SessionKind kind) => kind switch
    {
        SessionKind.Strength => "Strength",
        SessionKind.OutdoorCardio => "OutdoorCardio",
        SessionKind.Treadmill => "Treadmill",
        SessionKind.Mobility => "Mobility",
        SessionKind.GentleWalk => "GentleWalk",
        _ => throw Unnamed(kind),
    };

    public static SessionKind? SessionKindOf(string? name) => name switch
    {
        "Strength" => SessionKind.Strength,
        "OutdoorCardio" => SessionKind.OutdoorCardio,
        "Treadmill" => SessionKind.Treadmill,
        "Mobility" => SessionKind.Mobility,
        "GentleWalk" => SessionKind.GentleWalk,
        _ => null,
    };

    public static string Of(InkTargetKind kind) => kind switch
    {
        InkTargetKind.LargeTick => "LargeTick",
        InkTargetKind.StandardTick => "StandardTick",
        InkTargetKind.CircleLane => "CircleLane",
        InkTargetKind.NumberBox => "NumberBox",
        InkTargetKind.NoBecause => "NoBecause",
        InkTargetKind.NotesMargin => "NotesMargin",
        _ => throw Unnamed(kind),
    };

    public static InkTargetKind? InkTargetKindOf(string? name) => name switch
    {
        "LargeTick" => InkTargetKind.LargeTick,
        "StandardTick" => InkTargetKind.StandardTick,
        "CircleLane" => InkTargetKind.CircleLane,
        "NumberBox" => InkTargetKind.NumberBox,
        "NoBecause" => InkTargetKind.NoBecause,
        "NotesMargin" => InkTargetKind.NotesMargin,
        _ => null,
    };

    public static string Of(DiagnosticsOutcome outcome) => outcome switch
    {
        DiagnosticsOutcome.NeverRan => "NeverRan",
        DiagnosticsOutcome.Succeeded => "Succeeded",
        DiagnosticsOutcome.Refused => "Refused",
        DiagnosticsOutcome.Unreachable => "Unreachable",
        DiagnosticsOutcome.FoundNothingRead => "FoundNothingRead",
        DiagnosticsOutcome.Partial => "Partial",
        _ => throw Unnamed(outcome),
    };

    public static DiagnosticsOutcome? DiagnosticsOutcomeOf(string? name) => name switch
    {
        "NeverRan" => DiagnosticsOutcome.NeverRan,
        "Succeeded" => DiagnosticsOutcome.Succeeded,
        "Refused" => DiagnosticsOutcome.Refused,
        "Unreachable" => DiagnosticsOutcome.Unreachable,
        "FoundNothingRead" => DiagnosticsOutcome.FoundNothingRead,
        "Partial" => DiagnosticsOutcome.Partial,
        _ => null,
    };

    private static ArgumentOutOfRangeException Unnamed<T>(T value)
        where T : struct, Enum =>
        new(nameof(value), value, $"This {typeof(T).Name} has no stored word; add one to ReadBackWireNames.");
}
