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

    /// <summary>
    /// Dash → Post: one photo of a conversation, asked by its attachment id; Post answers on the same path with the picture
    /// shrunk on the phone to fit one message (P14 B5, W-5.1.3).
    /// </summary>
    public const string DashPhotoPath = "/dash/photo";

    /// <summary>
    /// Post → Dash: the phone's whole Rolodex, a page at a time, each contact its name and an id and never a number
    /// (P14 B6, W-5.1.2; PC6). Dash asks for each page on the same path.
    /// </summary>
    public const string DashRolodexPath = "/dash/rolodex";

    /// <summary>
    /// Post → Dash: a text just arrived that the phone itself alerts for, by its correspondence and whether its sender is
    /// starred in the phone's contacts, and nothing else (P14 B8): the wrist's two short taps (WD §7, A12). Told once, to a
    /// wrist in reach, and never queued; nothing answers it.
    /// </summary>
    public const string DashArrivedPath = "/dash/arrived";

    /// <summary>Every Dash path, in the plan's order, then the ones added after it.</summary>
    public static readonly IReadOnlyList<string> DashPaths =
    [
        DashPeoplePath, DashThreadPath, DashSendPath, DashFilePath, DashDeletePath, DashUndoPath, DashReadPath, DashCallNotePath, DashMailPath, DashPhotoPath,
        DashRolodexPath, DashArrivedPath,
    ];

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

    /// <summary>What Dash advertises (its <c>android_wear_capabilities</c>), so Post can find the wrist to tell it a text arrived (P14 B8).</summary>
    public const string DashCapability = "dash-wrist";

    /// <summary>Post's listener for every Dash path, registered by this name in Post's manifest.</summary>
    public const string DashListenerName = "com.storyworthtelling.focusphone.communication.DashListener";

    /// <summary>Post's preferences file for the relay.</summary>
    public const string DashPreferences = "dashRelay";

    /// <summary>The book of the wrist's acts, so a duplicated act stays done once across a restart.</summary>
    public const string DashActsKey = "acts";

    // ---- Dash's registered names (P14 B4): the watch app's screens, opened by these names ----

    /// <summary>People waiting, Dash's first screen and its launcher entry (A14); the People waiting tile opens it too.</summary>
    public const string DashPeopleActivityName = "com.storyworthtelling.focusphone.communication.dash.PeopleActivity";

    /// <summary>One conversation.</summary>
    public const string DashConversationActivityName = "com.storyworthtelling.focusphone.communication.dash.ConversationActivity";

    /// <summary>A reply read back before it is sent.</summary>
    public const string DashReplyActivityName = "com.storyworthtelling.focusphone.communication.dash.ReplyActivity";

    /// <summary>Filing or deleting a correspondence, then its Undo ring (P14 B5, W-5.1.4).</summary>
    public const string DashActActivityName = "com.storyworthtelling.focusphone.communication.dash.ActActivity";

    /// <summary>A new text: the phone's Rolodex, searched by a name typed or said (P14 B6, W-5.1.2).</summary>
    public const string DashRolodexActivityName = "com.storyworthtelling.focusphone.communication.dash.RolodexActivity";

    /// <summary>After the call: the note offered once a call has ended, dictated on the wrist (P14 B7, W-5.1.5).</summary>
    public const string DashAfterCallActivityName = "com.storyworthtelling.focusphone.communication.dash.AfterCallActivity";

    /// <summary>The mail lines: subject and sender, people only, never a buzz (P14 B7, W-5.1.6).</summary>
    public const string DashMailActivityName = "com.storyworthtelling.focusphone.communication.dash.MailActivity";

    public static readonly IReadOnlyList<string> DashComponents =
    [
        DashPeopleActivityName, DashConversationActivityName, DashReplyActivityName, DashActActivityName, DashRolodexActivityName,
        DashAfterCallActivityName, DashMailActivityName,
    ];

    /// <summary>
    /// Dash's ear for the watch's own call state (P14 B7): the receiver the platform wakes with the phone-state broadcast,
    /// registered by this name. Not a screen, so not among <see cref="DashComponents"/>.
    /// </summary>
    public const string DashCallReceiverName = "com.storyworthtelling.focusphone.communication.dash.CallEndReceiver";

    /// <summary>Dash's ear for a text arriving (P14 B8): Play services wakes it for <see cref="DashArrivedPath"/> alone.</summary>
    public const string DashArrivalListenerName = "com.storyworthtelling.focusphone.communication.dash.ArrivalListener";

    /// <summary>The People waiting tile (P14 B8, W-5.1.7; mock: Tile: People waiting), bound by the system by this name.</summary>
    public const string DashTileName = "com.storyworthtelling.focusphone.communication.dash.PeopleTileService";

    /// <summary>
    /// Dash's call screening (P14 B8, W-5.1.9; A12): the platform binds it, by this name, for a call from a number not in
    /// the watch's contacts, and it lets the call ring only when the same caller rang within minutes.
    /// </summary>
    public const string DashCallScreenName = "com.storyworthtelling.focusphone.communication.dash.CallScreen";

    /// <summary>
    /// What the call screening keeps across a restart (P14 B8): the last few unknown callers, each a keyed digest and a
    /// moment, for the repeat-caller window and no longer. Never a number.
    /// </summary>
    public const string DashCallersPreferences = "dash-callers";

    public const string DashCallersKey = "heard";

    /// <summary>The platform keystore's name for the key that seals a caller; it never leaves the keystore.</summary>
    public const string DashCallerSealAlias = "dash-caller-seal";

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
