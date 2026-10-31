using System.Globalization;

namespace Storyworthtelling.Diagnostics;

/// <summary>
/// One <c>ApplicationExitInfo</c>, reduced to plain values so the phrasing is testable off the device.
/// </summary>
/// <param name="Reason">ApplicationExitInfo.REASON_* (Android.App.ApplicationExitInfoReason).</param>
/// <param name="Status">The exit status or signal number.</param>
/// <param name="Description">The system's own description, when it gave one.</param>
/// <param name="Timestamp">Milliseconds since the epoch, as the platform reports it.</param>
/// <param name="Importance">The process importance at death (100 = foreground).</param>
public sealed record ExitRecord(int Reason, int Status, string? Description, long Timestamp, int Importance);

/// <summary>
/// G6 (LOG-LOOP-PLAN.md §4.3): what each way a process can end means, in a sentence. A force-stop, an
/// ANR or a system kill otherwise leaves nothing at all — the shape of Post's "stopped receiving texts".
/// Codes from the Mono.Android reference assembly's <c>Android.App.ApplicationExitInfoReason</c>.
/// </summary>
public static class ExitReasonPhrases
{
    private const int ForegroundImportance = 100;

    private static readonly Dictionary<int, (string Label, string Meaning, bool Problem)> Reasons = new()
    {
        [0] = ("UNKNOWN", "the process ended for a reason Android did not record", false),
        [1] = ("EXIT_SELF", "the app ended its own process", false),
        [2] = ("SIGNALED", "the process was killed by a signal", true),
        [3] = ("LOW_MEMORY", "Android killed the app to free memory", true),
        [4] = ("CRASH", "the app crashed (an uncaught exception)", true),
        [5] = ("CRASH_NATIVE", "the app crashed in native code", true),
        [6] = ("ANR", "the app stopped responding and Android ended it", true),
        [7] = ("INITIALIZATION_FAILURE", "the process failed while starting", true),
        [8] = ("PERMISSION_CHANGE", "a permission was changed, which restarts the app", false),
        [9] = ("EXCESSIVE_RESOURCE_USAGE", "Android killed the app for using too many resources", true),
        [10] = ("USER_REQUESTED", "the owner closed the app (for example from Recents)", false),
        [11] = ("USER_STOPPED", "the app was force-stopped; Android delivers nothing to it until it is opened again", true),
        [12] = ("DEPENDENCY_DIED", "a process the app depends on died", true),
        [13] = ("OTHER", "Android ended the process for a reason it classes as other", true),
        [14] = ("FREEZER", "the frozen process was killed", true),
        [15] = ("PACKAGE_STATE_CHANGE", "the app was disabled or its state changed", false),
        [16] = ("PACKAGE_UPDATED", "the app was updated", false),
    };

    public static string Label(int reason) =>
        Reasons.TryGetValue(reason, out var known) ? known.Label : string.Create(CultureInfo.InvariantCulture, $"REASON_{reason}");

    /// <summary>Whether this is an exit nobody asked for — the ones worth a line.</summary>
    public static bool IsProblem(int reason) => !Reasons.TryGetValue(reason, out var known) || known.Problem;

    public static string Describe(ExitRecord record)
    {
        var meaning = Reasons.TryGetValue(record.Reason, out var known)
            ? known.Meaning
            : "Android ended the process for a reason this app does not know";
        var where = record.Importance == ForegroundImportance ? " (while in the foreground)" : string.Empty;
        var system = string.IsNullOrWhiteSpace(record.Description) ? string.Empty : $" System says: {record.Description.Trim()}.";
        return $"{Label(record.Reason)}: {meaning}{where}.{system}";
    }

    /// <summary>The exits not yet logged, oldest first.</summary>
    public static IReadOnlyList<ExitRecord> NewSince(IEnumerable<ExitRecord> records, long lastSeenTimestamp) =>
        records.Where(r => r.Timestamp > lastSeenTimestamp).OrderBy(r => r.Timestamp).ToList();
}
