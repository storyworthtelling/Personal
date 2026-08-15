namespace FocusPhone.Domain.Updates;

/// <summary>What happened to a single app the update channel considered.</summary>
public enum InstallOutcome
{
    /// A newer (or first-time) APK was installed successfully.
    Installed,

    /// The installed build was already current, so nothing was installed.
    AlreadyCurrent,

    /// An install was attempted and the platform rejected it (e.g. a signature
    /// mismatch against a sideloaded build). Deliberately not remembered as
    /// processed so the next poll retries it.
    Failed,

    /// The downloaded asset wasn't a readable APK; skipped and remembered so it
    /// isn't re-downloaded.
    Unreadable,
}

/// <summary>The outcome of one app the channel looked at, with its version and any error text.</summary>
public record AppInstallResult(string Package, long VersionCode, InstallOutcome Outcome, string? Error = null);

/// <summary>Why a whole update run ended the way it did.</summary>
public enum UpdateRunState
{
    /// The channel fetched the release and evaluated its assets (see <see cref="UpdateRunResult.Apps"/>).
    Ran,

    /// No GitHub token is configured, so the channel can't read the release; it skipped silently.
    NoToken,

    /// The app isn't the device owner, so silent install is unavailable; it skipped.
    NotDeviceOwner,

    /// The release fetch or a download failed (transient network or a rejected token).
    FetchFailed,
}

/// <summary>
/// The result of one update-channel run, made observable so the worker and the
/// admin "Install from GitHub now" action can report what actually happened —
/// installed, already current, or failed — instead of a commit that silently
/// swallowed a failed install. Pure so the summary wording is unit-tested away
/// from Android.
/// </summary>
public record UpdateRunResult(UpdateRunState State, IReadOnlyList<AppInstallResult> Apps, string? Error = null)
{
    public static UpdateRunResult NoToken() => new(UpdateRunState.NoToken, []);

    public static UpdateRunResult NotDeviceOwner() => new(UpdateRunState.NotDeviceOwner, []);

    public static UpdateRunResult FetchFailed(string error) => new(UpdateRunState.FetchFailed, [], error);

    public static UpdateRunResult Ran(IReadOnlyList<AppInstallResult> apps) => new(UpdateRunState.Ran, apps);

    public bool AnyInstalled => Apps.Any(a => a.Outcome == InstallOutcome.Installed);

    public bool AnyFailed => Apps.Any(a => a.Outcome == InstallOutcome.Failed);

    /// <summary>
    /// A quiet, single-sentence status line for the settings panel (DESIGN.md §6:
    /// loading/state is a sentence, not a spinner or a toast).
    /// </summary>
    public string Summarize() => State switch
    {
        UpdateRunState.NoToken => "No update token is set, so updates can't be checked.",
        UpdateRunState.NotDeviceOwner => "Not the device owner, so updates can't install.",
        UpdateRunState.FetchFailed => $"Couldn't reach GitHub ({Error}).",
        _ => SummarizeRun(),
    };

    private string SummarizeRun()
    {
        var installed = Apps.Count(a => a.Outcome == InstallOutcome.Installed);
        var failed = Apps.Where(a => a.Outcome == InstallOutcome.Failed).ToList();

        var parts = new List<string>();
        if (installed > 0)
        {
            parts.Add($"Installed {installed} update{(installed == 1 ? "" : "s")}.");
        }
        if (failed.Count > 0)
        {
            var reason = failed[0].Error;
            var detail = string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})";
            parts.Add($"{failed.Count} couldn't install{detail}.");
        }
        return parts.Count == 0 ? "Everything is up to date." : string.Join(" ", parts);
    }
}

/// <summary>Rules about update outcomes that must stay identical across every caller.</summary>
public static class InstallOutcomes
{
    /// <summary>
    /// A failed install is deliberately NOT remembered as processed, so the next
    /// poll re-downloads and retries it (self-healing once the blocker clears);
    /// every other outcome is settled and remembered so an unchanged asset isn't
    /// re-fetched on every 6-hour cycle.
    /// </summary>
    public static bool ShouldMarkProcessed(InstallOutcome outcome) => outcome != InstallOutcome.Failed;
}
