namespace Codex.Domain.Diagnostics;

/// <summary>
/// Plate's key actions (LOG-LOOP-PLAN.md §4.1, O10): the taps that start background work, declared to the
/// tap watch so they are never judged dead and their failure or silence is logged. Wire names in the logs,
/// listed by <c>KeyActionsDeclaredTests</c>, so a new one is a deliberate edit.
/// </summary>
public static class PlateKeyActions
{
    /// <summary>The shutter, answered when the capture is saved to the Darkroom.</summary>
    public const string Shutter = "plate.shutter";

    /// <summary>Darkroom: Send, answered by the DCIM insert.</summary>
    public const string Send = "plate.send";

    /// <summary>Scans: opening the page scanner.</summary>
    public const string Scan = "plate.scan";

    /// <summary>Scans: delivering the assembled document.</summary>
    public const string Deliver = "plate.deliver";

    /// <summary>Faces: recognising the people in a photo.</summary>
    public const string Recognise = "plate.recognise";

    /// <summary>Diagnostics: Send logs now.</summary>
    public const string SendLogsNow = "plate.send-logs-now";

    public static IReadOnlyList<string> All { get; } =
        [Shutter, Send, Scan, Deliver, Recognise, SendLogsNow];
}
