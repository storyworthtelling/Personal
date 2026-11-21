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
