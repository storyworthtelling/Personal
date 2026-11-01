namespace Codex.Domain.Diagnostics;

/// <summary>
/// Flyleaf's key actions (LOG-LOOP-PLAN.md §4.1, O10): the taps that start background work, declared to the
/// tap watch so they are never judged dead and their failure or silence is logged. Wire names in the logs,
/// listed by <c>KeyActionsDeclaredTests</c>, so a new one is a deliberate edit.
/// </summary>
public static class FlyleafKeyActions
{
    /// <summary>Settings: Install from GitHub now.</summary>
    public const string InstallFromGitHub = "flyleaf.install-from-github";

    /// <summary>Settings: Install APK from Downloads.</summary>
    public const string InstallApk = "flyleaf.install-apk";

    /// <summary>Unlock: starting or entering an Admin session.</summary>
    public const string AdminUnlock = "flyleaf.admin-unlock";

    /// <summary>A book's action card: Move to shelf.</summary>
    public const string MoveToShelf = "flyleaf.move-to-shelf";

    /// <summary>A book's action card: Feature on home.</summary>
    public const string Feature = "flyleaf.feature";

    /// <summary>A book's action card: Block.</summary>
    public const string Block = "flyleaf.block";

    /// <summary>A book's action card: Uninstall, answered by the platform's result.</summary>
    public const string Uninstall = "flyleaf.uninstall";

    /// <summary>Settings: a Wi-Fi, mobile data or Bluetooth toggle.</summary>
    public const string Connectivity = "flyleaf.connectivity";

    /// <summary>Home: Answer the call / Return to the call.</summary>
    public const string AnswerCall = "flyleaf.answer-call";

    /// <summary>Diagnostics: Send logs now.</summary>
    public const string SendLogsNow = "flyleaf.send-logs-now";

    public static IReadOnlyList<string> All { get; } =
        [InstallFromGitHub, InstallApk, AdminUnlock, MoveToShelf, Feature, Block, Uninstall, Connectivity, AnswerCall, SendLogsNow];
}
