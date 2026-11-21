using System.Text.Json;

namespace Watch.Domain.Updates;

/// <summary>What one APK the phone sent came to on the watch (P14 W-3.1.2).</summary>
public enum WatchInstallOutcome
{
    /// <summary>Installed with nobody asked: an update by its installer of record.</summary>
    Installed,

    /// <summary>Installed once the wrist said yes: a first install, or an update the platform would not take silently.</summary>
    InstalledAfterAsking,

    /// <summary>The system's install prompt is waiting on the wrist.</summary>
    Waiting,

    /// <summary>The person turned the prompt down, or it was abandoned.</summary>
    Declined,

    AlreadyCurrent,

    /// <summary>A phone APK, or one whose form could not be read or whose readings disagree.</summary>
    NotForThisWatch,

    /// <summary>Outside the suite's namespace.</summary>
    NotOurs,

    /// <summary>The bytes were not the APK the phone described: cut short, changed, or not an APK at all.</summary>
    Damaged,

    /// <summary>The platform refused the install (see the <see cref="InstallerStatus"/> beside it).</summary>
    Failed,
}

/// <summary>
/// The platform's install status, as the watch's own words. The Android half maps <c>PackageInstallStatus</c> onto it
/// by name, so no platform number is copied here.
/// </summary>
public enum InstallerStatus
{
    PendingUserAction,
    Success,
    Failure,
    Blocked,
    Aborted,
    Invalid,
    Conflict,
    Storage,
    Incompatible,
    Timeout,

    /// <summary>A status this build does not know: a newer platform's.</summary>
    Unrecognised,
}

/// <summary>The last outcome of one app's install; the status is the platform's word for a failure.</summary>
public sealed record WatchInstallResult(string Package, long VersionCode, WatchInstallOutcome Outcome, InstallerStatus? Status = null);

/// <summary>What an outcome means, and its words on the wire.</summary>
public static class WatchInstallOutcomes
{
    /// <summary>The outcome of a platform status. <paramref name="askedBefore"/>: the same install had been waiting on the wrist.</summary>
    public static WatchInstallOutcome FromStatus(InstallerStatus status, bool askedBefore) => status switch
    {
        InstallerStatus.PendingUserAction => WatchInstallOutcome.Waiting,
        InstallerStatus.Success => askedBefore ? WatchInstallOutcome.InstalledAfterAsking : WatchInstallOutcome.Installed,
        InstallerStatus.Aborted => WatchInstallOutcome.Declined,
        _ => WatchInstallOutcome.Failed,
    };

    /// <summary>
    /// Whether the phone should send the same APK again on its next run. Only for a transfer or an install that failed:
    /// a prompt waiting on the wrist, or one the person turned down, is not answered by asking again, and a newer build
    /// is a new APK anyway.
    /// </summary>
    public static bool SendAgain(WatchInstallOutcome outcome) =>
        outcome is WatchInstallOutcome.Damaged or WatchInstallOutcome.Failed;

    // Wire words, frozen: the phone reads them off update/installed.
    private static readonly (WatchInstallOutcome Outcome, string Word)[] OutcomeWords =
    [
        (WatchInstallOutcome.Installed, "installed"),
        (WatchInstallOutcome.InstalledAfterAsking, "installed-after-asking"),
        (WatchInstallOutcome.Waiting, "waiting"),
        (WatchInstallOutcome.Declined, "declined"),
        (WatchInstallOutcome.AlreadyCurrent, "current"),
        (WatchInstallOutcome.NotForThisWatch, "not-for-this-watch"),
        (WatchInstallOutcome.NotOurs, "not-ours"),
        (WatchInstallOutcome.Damaged, "damaged"),
        (WatchInstallOutcome.Failed, "failed"),
    ];

    private static readonly (InstallerStatus Status, string Word)[] StatusWords =
    [
        (InstallerStatus.PendingUserAction, "pending-user-action"),
        (InstallerStatus.Success, "success"),
        (InstallerStatus.Failure, "failure"),
        (InstallerStatus.Blocked, "blocked"),
        (InstallerStatus.Aborted, "aborted"),
        (InstallerStatus.Invalid, "invalid"),
        (InstallerStatus.Conflict, "conflict"),
        (InstallerStatus.Storage, "storage"),
        (InstallerStatus.Incompatible, "incompatible"),
        (InstallerStatus.Timeout, "timeout"),
        (InstallerStatus.Unrecognised, "unrecognised"),
    ];

    public static string Word(WatchInstallOutcome outcome) => OutcomeWords.Single(pair => pair.Outcome == outcome).Word;

    /// <summary>The outcome a word names, exactly as written; anything else is none.</summary>
    public static WatchInstallOutcome? FromWord(string? word) =>
        OutcomeWords.Where(pair => string.Equals(pair.Word, word, StringComparison.Ordinal)).Select(pair => (WatchInstallOutcome?)pair.Outcome).FirstOrDefault();

    public static string Word(InstallerStatus status) => StatusWords.Single(pair => pair.Status == status).Word;

    public static InstallerStatus? StatusFromWord(string? word) =>
        StatusWords.Where(pair => string.Equals(pair.Word, word, StringComparison.Ordinal)).Select(pair => (InstallerStatus?)pair.Status).FirstOrDefault();
}

/// <summary>The watch's record of the last outcome per app, kept in its preferences and reported to the phone.</summary>
public static class InstallResults
{
    /// <summary>
    /// The record with <paramref name="result"/> in place of whatever the same app had, unless that was a newer build's:
    /// a newer build's prompt abandons the older one's session, whose end can be reported after it.
    /// </summary>
    public static IReadOnlyList<WatchInstallResult> Record(IReadOnlyList<WatchInstallResult> results, WatchInstallResult result)
    {
        var existing = results.FirstOrDefault(entry => string.Equals(entry.Package, result.Package, StringComparison.Ordinal));
        if (existing is not null && existing.VersionCode > result.VersionCode)
        {
            return results;
        }
        return [.. results.Where(entry => !string.Equals(entry.Package, result.Package, StringComparison.Ordinal)), result];
    }

    /// <summary>Whether this very install (package and version) was waiting on the wrist.</summary>
    public static bool WasWaiting(IReadOnlyList<WatchInstallResult> results, string package, long versionCode) =>
        results.Any(result => string.Equals(result.Package, package, StringComparison.Ordinal)
            && result.VersionCode == versionCode
            && result.Outcome == WatchInstallOutcome.Waiting);

    public static string Encode(IReadOnlyList<WatchInstallResult> results)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            InstalledCodec.WriteResults(writer, results);
        }
        return System.Text.Encoding.UTF8.GetString(json.ToArray());
    }

    /// <summary>The stored record, or none: a record that will not read is a convenience lost, not a failure.</summary>
    public static IReadOnlyList<WatchInstallResult> Decode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(stored);
            return InstalledCodec.ReadResults(document.RootElement) ?? [];
        }
        catch (JsonException)
        {
            // Damaged on the watch: start a fresh record; the phone still learns the versions from the report.
            return [];
        }
    }
}

/// <summary>
/// The installer's log lines (plan §5): a contract and its outcome, an app and what its install came to, and the
/// platform's status word for a failure. Never an APK's bytes, a message's body or the platform's free-text message.
/// </summary>
public static class UpdateLines
{
    public const string ApkContract = "update/apk";

    public const string InstalledContract = "update/installed";

    public static string Install(string package, long versionCode, WatchInstallOutcome outcome, InstallerStatus? status = null, bool silenceRequested = false)
    {
        var line = $"UPDATE {package} {versionCode}: {outcome}";
        if (outcome == WatchInstallOutcome.Failed && status is { } failure)
        {
            return $"{line} — {WatchInstallOutcomes.Word(failure)}";
        }
        if (outcome == WatchInstallOutcome.Waiting && silenceRequested)
        {
            return $"{line} — asked although silence was requested";
        }
        return line;
    }

    public static string Receipt<T>(string contract, ContractRead<T> read) where T : class => read.Refusal switch
    {
        ContractRefusal.None => $"CONTRACT {contract}: received v{read.Version}",
        ContractRefusal.Newer => $"CONTRACT {contract}: refused v{read.Version}, newer than this watch reads",
        _ => $"CONTRACT {contract}: refused, unreadable",
    };

    /// <summary>The prompt could not be put on the wrist: notifications are off for the installer (granted once, by adb).</summary>
    public static string CannotAsk(string package, long versionCode) =>
        $"{Install(package, versionCode, WatchInstallOutcome.Waiting)} — notifications are off, so nothing reached the wrist";

    public static string Sent(string contract) => $"CONTRACT {contract}: sent v{InstalledCodec.Version}";

    /// <param name="failure">The exception's type name; never its message.</param>
    public static string NotSent(string contract, string failure) => $"CONTRACT {contract}: not sent, {failure}";

    /// <summary>A channel that closed before the APK had arrived; <paramref name="closed"/> is the close reason's word.</summary>
    public static string Transfer(string contract, string closed) => $"CONTRACT {contract}: transfer ended, {closed}";
}
