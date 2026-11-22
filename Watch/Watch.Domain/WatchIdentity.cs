namespace Watch.Domain;

/// <summary>
/// The watch's frozen identifiers (P14 plan §4), each a string literal in this one class and held by
/// <c>WatchInvariantsTests</c>. None of them fails at build time if it moves: the Data Layer delivers only between apps of
/// one package and one signature, a listener hears only the paths it names, the platform remembers a component by its
/// registered name and a preference by its key. A literal, never <c>nameof</c>: renaming a type must not rename a wire.
/// </summary>
public static class WatchIdentity
{
    // ---- packages (plan §2.2, PC1): each watch app carries its phone app's package; the face has its own ----

    /// <summary>Dash, Post's watch half.</summary>
    public const string DashPackage = "com.storyworthtelling.focusphone.communication";

    /// <summary>Wave, Score's watch half.</summary>
    public const string WavePackage = "com.storyworthtelling.focusphone.audio";

    /// <summary>Beat, Constitution's watch half.</summary>
    public const string BeatPackage = "com.storyworthtelling.constitution";

    /// <summary>The Point installer, Flyleaf's watch half (PC8): Flyleaf sends it every watch APK.</summary>
    public const string InstallerPackage = "com.storyworthtelling.focusphone.launcher";

    /// <summary>Point, the face: code-free, so it needs no phone partner and has a package of its own.</summary>
    public const string FacePackage = "com.storyworthtelling.watch.point";

    /// <summary>Every suite package that can be on the watch, in a fixed order.</summary>
    public static readonly IReadOnlyList<string> Packages = [DashPackage, WavePackage, BeatPackage, InstallerPackage, FacePackage];

    // ---- the Data Layer (plan §2.5) ----

    /// <summary>
    /// Flyleaf → installer, over <c>ChannelClient</c>: a channel opened on this path, then <c>/</c> and the transfer's own
    /// name (<c>UpdatePaths</c>), carries a versioned header and one APK.
    /// </summary>
    public const string UpdateApkPath = "/update/apk";

    /// <summary>Flyleaf ↔ installer, over <c>MessageClient</c>: the phone asks what the watch has, the watch reports it.</summary>
    public const string UpdateInstalledPath = "/update/installed";

    /// <summary>What the installer advertises (its <c>android_wear_capabilities</c>), so Flyleaf can find the watch.</summary>
    public const string InstallerCapability = "point-installer";

    // ---- Dash's contracts (plan §2.5, P14 W-4.1.1): Post on the phone, Dash on the watch ----

    /// <summary>Post → Dash: the correspondences waiting. Dash asks on the same path.</summary>
    public const string DashPeoplePath = "/dash/people";

    /// <summary>Post → Dash: the last messages of one correspondence. Dash asks on the same path.</summary>
    public const string DashThreadPath = "/dash/thread";

    /// <summary>Dash → Post: a reply or a new text; Post answers how it went on the same path.</summary>
    public const string DashSendPath = "/dash/send";

    /// <summary>Dash → Post: file a correspondence or some of its messages.</summary>
    public const string DashFilePath = "/dash/file";

    /// <summary>Dash → Post: delete a correspondence or some of its messages, undo-first.</summary>
    public const string DashDeletePath = "/dash/delete";

    /// <summary>Dash → Post: take back a file or a delete inside its window.</summary>
    public const string DashUndoPath = "/dash/undo";

    /// <summary>Dash → Post: a correspondence read on the watch is read on the phone (A14).</summary>
    public const string DashReadPath = "/dash/read";

    /// <summary>Dash → Post: the note dictated for a call (A13).</summary>
    public const string DashCallNotePath = "/dash/call-note";

    /// <summary>Post → Dash: people-only mail lines (A11). Dash asks on the same path.</summary>
    public const string DashMailPath = "/dash/mail";

    /// <summary>Every Dash path, in the plan's order.</summary>
    public static readonly IReadOnlyList<string> DashPaths =
        [DashPeoplePath, DashThreadPath, DashSendPath, DashFilePath, DashDeletePath, DashUndoPath, DashReadPath, DashCallNotePath, DashMailPath];

    // ---- the handed-over Google token (plan §2.4, §2.5, P14 W-4.1.2) ----

    /// <summary>
    /// A phone app ↔ its watch app (the Data Layer joins only one package to itself), over <c>MessageClient</c>: the watch
    /// app asks when it needs a token and the phone answers on the same path. Only a read-only Drive token crosses
    /// (<c>KeyCodec</c>), for Wave's streaming; Dash needs none, since Post relays the mail lines.
    /// </summary>
    public const string GoogleKeysPath = "/keys/google";

    // ---- Post's half of Dash (P14 B2): the phone end of the same contracts ----

    /// <summary>What Post advertises (its <c>android_wear_capabilities</c>), so Dash can find the phone that carries the texts.</summary>
    public const string PostCapability = "post-relay";

    /// <summary>Post's listener for every Dash path, registered by this name in Post's manifest.</summary>
    public const string DashListenerName = "com.storyworthtelling.focusphone.communication.DashListener";

    /// <summary>Post's preferences file for the relay.</summary>
    public const string DashPreferences = "dashRelay";

    /// <summary>The book of the wrist's acts, so a duplicated act stays done once across a restart.</summary>
    public const string DashActsKey = "acts";

    // ---- the installer's registered names ----

    public const string UpdateListenerName = "com.storyworthtelling.focusphone.launcher.point.UpdateListenerService";

    public const string InstallStatusReceiverName = "com.storyworthtelling.focusphone.launcher.point.InstallStatusReceiver";

    public const string ReplacedReceiverName = "com.storyworthtelling.focusphone.launcher.point.ReplacedReceiver";

    public static readonly IReadOnlyList<string> InstallerComponents = [UpdateListenerName, InstallStatusReceiverName, ReplacedReceiverName];

    // ---- what the installer keeps on the watch ----

    /// <summary>The installer's preferences file.</summary>
    public const string InstallerPreferences = "point-installer";

    /// <summary>The last outcome of each app's install, reported to the phone on <see cref="UpdateInstalledPath"/>.</summary>
    public const string ResultsKey = "results";

    /// <summary>The silent notification channel the wrist is asked through. Android keeps a channel by its id.</summary>
    public const string InstallChannel = "point-install";

    /// <summary>Each app's current install session and the sessions the installer abandoned itself (release A fix
    /// round, finding 5), so a status is judged by the session it is for.</summary>
    public const string SessionsKey = "sessions";

    // ---- the installer's status intent: its extras outlive the process that made it, and the installer's own update ----

    public const string StatusPackageExtra = "package";

    public const string StatusVersionCodeExtra = "versionCode";

    public const string StatusActionExtra = "action";

    public const string StatusSilenceExtra = "silence";

    /// <summary>The session the status is for.</summary>
    public const string StatusSessionExtra = "session";

    // ---- Flyleaf's half (W-3.1.3): the phone end of the same contract ----

    /// <summary>Flyleaf's listener for the watch's reports, registered by this name in Flyleaf's manifest.</summary>
    public const string PhoneReportListenerName = "com.storyworthtelling.focusphone.launcher.WatchReportListener";

    /// <summary>Flyleaf's preferences file for the watch half: what it sent, and the last run.</summary>
    public const string PhonePreferences = "watchUpdates";

    /// <summary>What the phone last sent the watch, one entry per app.</summary>
    public const string SentKey = "sent";

    /// <summary>The watch's last run, for Settings' quiet line.</summary>
    public const string LastRunKey = "lastRun";
}
