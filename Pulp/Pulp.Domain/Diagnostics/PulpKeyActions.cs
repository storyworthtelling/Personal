namespace Pulp.Domain.Diagnostics;

/// <summary>
/// Pulp's key actions (LOG-LOOP-PLAN.md §4.1, O10): the taps that start background work, declared to the
/// tap watch so they are never judged dead and their failure or silence is logged. Wire names in the logs,
/// listed by <c>KeyActionsDeclaredTests</c>, so a new one is a deliberate edit.
/// </summary>
public static class PulpKeyActions
{
    /// <summary>Handing a title to its service's own app.</summary>
    public const string HandOff = "pulp.hand-off";

    /// <summary>Asking Flyleaf for a locked hand-off.</summary>
    public const string LockedOpen = "pulp.locked-open";

    /// <summary>Planning a title or a game onto the household calendar.</summary>
    public const string Plan = "pulp.plan";

    /// <summary>Settings: writing the Pulp calendar now.</summary>
    public const string CalendarWrite = "pulp.calendar-write";

    /// <summary>Diagnostics: Send logs now.</summary>
    public const string SendLogsNow = "pulp.send-logs-now";

    public static IReadOnlyList<string> All { get; } =
        [HandOff, LockedOpen, Plan, CalendarWrite, SendLogsNow];
}
