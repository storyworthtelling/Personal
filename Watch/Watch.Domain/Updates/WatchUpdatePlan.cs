using Storyworthtelling.Updates;

namespace Watch.Domain.Updates;

/// <summary>A suite app on the watch: its versionCode, and whether this installer is its installer of record.</summary>
public sealed record InstalledApp(string Package, long VersionCode, bool OfRecord);

/// <summary>What the installer does with an APK.</summary>
public enum WatchUpdateAction
{
    Refuse,

    /// <summary>Not on the watch yet: the platform asks the wrist, once (A70).</summary>
    FirstInstall,

    /// <summary>An update to an app another installer put there (by hand, with adb): the wrist is asked once, after
    /// which this installer is its installer of record.</summary>
    AskedUpdate,

    /// <summary>An update this installer may make silently: its installer of record, or itself.</summary>
    SilentUpdate,
}

/// <summary>The decision, and for a refusal the outcome that says why.</summary>
public sealed record WatchUpdateDecision(WatchUpdateAction Action, WatchInstallOutcome? Refusal)
{
    public bool Install => Action != WatchUpdateAction.Refuse;

    /// <summary>The platform will put its own prompt in front of the person.</summary>
    public bool AsksTheWrist => Action is WatchUpdateAction.FirstInstall or WatchUpdateAction.AskedUpdate;

    /// <summary>The session asks for <c>USER_ACTION_NOT_REQUIRED</c>.</summary>
    public bool RequestsSilence => Action == WatchUpdateAction.SilentUpdate;

    internal static WatchUpdateDecision Refused(WatchInstallOutcome why) => new(WatchUpdateAction.Refuse, why);
}

/// <summary>
/// What the watch read from the APK that arrived: the platform's package and versionCode (null package when it could not
/// read the APK at all), and its form read twice, from the manifest's bytes and from the platform's uses-feature list.
/// </summary>
public sealed record ApkReading(string? Package, long VersionCode, ApkForm Scanned, ApkForm Declared);

/// <summary>
/// The Point installer's decision for each APK the phone sends (P14 W-3.1.2), on the update channel's own rule
/// (<see cref="SiblingUpdatePlan"/> with the watch form) and then the one question a watch adds: whether the wrist has
/// to be asked.
///
/// The form comes first. A watch app carries its phone app's package, so a phone APK on the watch reads as "not
/// installed yet", the one reading that would install it; the installer refuses it itself, whatever the phone decided.
/// </summary>
public static class WatchUpdatePlan
{
    /// <summary>The decision on what the phone says it is sending (its header), before a byte is trusted.</summary>
    public static WatchUpdateDecision Decide(InstallCandidate offered, IReadOnlyCollection<InstalledApp> installed)
    {
        if (offered.Form != ApkForm.Watch)
        {
            return WatchUpdateDecision.Refused(WatchInstallOutcome.NotForThisWatch);
        }
        if (!SuiteApps.IsOwnApp(offered.Package))
        {
            return WatchUpdateDecision.Refused(WatchInstallOutcome.NotOurs);
        }

        var versions = installed.ToDictionary(app => app.Package, app => app.VersionCode, StringComparer.Ordinal);
        var plan = SiblingUpdatePlan.Decide(UpdateRole.Custodian, WatchIdentity.InstallerPackage, ApkForm.Watch, [new ReleaseApk(0, offered)], versions);
        if (plan.Single().Settled is { } settled)
        {
            return WatchUpdateDecision.Refused(settled == InstallOutcome.NotForThisDevice ? WatchInstallOutcome.NotForThisWatch : WatchInstallOutcome.AlreadyCurrent);
        }

        var existing = installed.FirstOrDefault(app => string.Equals(app.Package, offered.Package, StringComparison.Ordinal));
        if (existing is null)
        {
            return new WatchUpdateDecision(WatchUpdateAction.FirstInstall, null);
        }

        // An app may update itself, and the installer of record may update what it installed (Android 12+, with
        // UPDATE_PACKAGES_WITHOUT_USER_ACTION). The installer was put on the watch with adb, so it is never its own of record.
        var silent = existing.OfRecord || string.Equals(offered.Package, WatchIdentity.InstallerPackage, StringComparison.Ordinal);
        return new WatchUpdateDecision(silent ? WatchUpdateAction.SilentUpdate : WatchUpdateAction.AskedUpdate, null);
    }

    /// <summary>
    /// The decision on what actually arrived. The bytes must be whole and be the APK the header described, and the
    /// header's form, the manifest's and the platform's must all say watch; then the version rule is applied again on
    /// the APK's own package and version.
    /// </summary>
    public static WatchUpdateDecision Verify(ReceivedApk received, ApkReading reading, IReadOnlyCollection<InstalledApp> installed)
    {
        var header = received.Header;
        if (!received.Intact || reading.Package is null
            || !string.Equals(reading.Package, header.Package, StringComparison.Ordinal)
            || reading.VersionCode != header.VersionCode)
        {
            return WatchUpdateDecision.Refused(WatchInstallOutcome.Damaged);
        }

        var form = ApkForms.Agree(header.Form, reading.Scanned, reading.Declared);
        return Decide(new InstallCandidate(reading.Package, reading.VersionCode, form), installed);
    }
}

/// <summary>
/// The one thing the wrist is asked (WD §6, §11): one sentence on a silent card, which opens the system's own install
/// prompt. Names are the apps' watch names (A47, A48).
/// </summary>
public static class InstallPrompt
{
    /// <summary>The installer's label: what the system's prompts and the watch's settings call it.</summary>
    public const string InstallerLabel = "Point installer";

    /// <summary>The name of the installer's one notification channel, as the watch's settings show it.</summary>
    public const string ChannelName = "Installs";

    private static readonly (string Package, string Name)[] Names =
    [
        (WatchIdentity.DashPackage, "Dash"),
        (WatchIdentity.WavePackage, "Wave"),
        (WatchIdentity.BeatPackage, "Beat"),
        (WatchIdentity.InstallerPackage, InstallerLabel),
        (WatchIdentity.FacePackage, "Point"),
    ];

    // A suite app this build has no name for (one added after it) still gets a card of its own.
    private const int OtherAppNotification = 100;

    public static string NameOf(string package) =>
        Names.Where(pair => string.Equals(pair.Package, package, StringComparison.Ordinal)).Select(pair => pair.Name).FirstOrDefault()
        ?? "a watch app";

    public static string Sentence(string package, WatchUpdateAction action) => action switch
    {
        WatchUpdateAction.FirstInstall => $"Tap to install {NameOf(package)}.",
        WatchUpdateAction.Refuse => throw new ArgumentOutOfRangeException(nameof(action), "A refused APK asks nothing."),
        _ => $"Tap to update {NameOf(package)}.",
    };

    /// <summary>One card per app, so a newer build's prompt replaces its older one rather than piling up.</summary>
    public static int NotificationId(string package)
    {
        var index = WatchIdentity.Packages.ToList().IndexOf(package);
        return index >= 0 ? index + 1 : OtherAppNotification;
    }
}
