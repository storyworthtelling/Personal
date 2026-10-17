namespace Constitution.Domain;

/// <summary>
/// The identifiers that must never move once Constitution has shipped a single build, in one
/// place.
///
/// <para>Every one of these is invisible: nothing fails at build time if it changes, and nothing
/// fails loudly at run time either. The phone keeps the old app and never sees another update, a
/// worker stops running with no log the owner can reach, or the app comes up with empty settings.
/// <c>Constitution.Tests/Guard/ConstitutionInvariantsTests</c> is what fails instead.</para>
///
/// <para>It lives in Domain because Domain has no dependencies of its own to load and every layer
/// can see it, including the desktop test build, which is what lets the guards run with no Android
/// workload installed. Java names are built from <see cref="ApplicationId"/> rather than typed out,
/// so the package id exists as exactly one literal. Every value here is a string literal, never
/// derived from an identifier: renaming a property would otherwise rename the stored key with it,
/// and the stored value would simply not be found (Folio's and Foreword's lesson).</para>
///
/// <para>Growing a set is a deliberate edit to the guard's expected list, not a drift.</para>
/// </summary>
public static class ConstitutionIdentity
{
    /// <summary>
    /// Frozen from the first build. A device recognises an update only when the package name is
    /// unchanged, and the device-owner update channel installs by package id: rename it and the
    /// phone keeps the old Constitution installed and never sees another update.
    /// </summary>
    public const string ApplicationId = "com.storyworthtelling.constitution";

    /// <summary>
    /// The launcher activity's registered Java name. Without an explicit registration .NET for
    /// Android derives it from the assembly and namespace, so a later rename would move it and
    /// break the home-screen entry that points at it.
    /// </summary>
    public const string MainActivityName = ApplicationId + ".MainActivity";

    /// <summary>
    /// The morning read-back worker's registered Java name. WorkManager persists a scheduled row
    /// by class name and periodic work is enqueued with <c>Keep</c>, so a moved class is one
    /// WorkManager can no longer instantiate and the job silently stops running.
    /// </summary>
    public const string ReadBackWorkerName = ApplicationId + ".ReadBackWorker";

    /// <summary>The Body sync worker's registered Java name; frozen for the same reason as <see cref="ReadBackWorkerName"/>.</summary>
    public const string BodySyncWorkerName = ApplicationId + ".BodySyncWorker";

    /// <summary>
    /// The read-back worker's unique work name, persisted beside the class name. Change it and the
    /// old schedule is never replaced, so the phone runs two schedules or none.
    /// </summary>
    public const string ReadBackWorkName = "constitution-read-back";

    /// <summary>The Body sync worker's unique work name; frozen for the same reason as <see cref="ReadBackWorkName"/>.</summary>
    public const string BodySyncWorkName = "constitution-body-sync";

    /// <summary>
    /// The app's SharedPreferences file name. It is where an upgraded phone finds what the owner
    /// set; rename it and every setting is silently not found.
    /// </summary>
    public const string SettingsPreferencesName = "constitution-settings";

    /// <summary>
    /// Which pillars this install shows, one boolean each, inside <see cref="SettingsPreferencesName"/>.
    /// Literals, not derived: a renamed property must not rename the stored key. Absent means on.
    /// </summary>
    public const string PillarWorkoutsKey = "pillar-workouts";

    /// <summary>The Meals pillar's switch; see <see cref="PillarWorkoutsKey"/>.</summary>
    public const string PillarMealsKey = "pillar-meals";

    /// <summary>The Body pillar's switch; see <see cref="PillarWorkoutsKey"/>.</summary>
    public const string PillarBodyKey = "pillar-body";

    /// <summary>Every registered worker Java name, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> Workers { get; } = [ReadBackWorkerName, BodySyncWorkerName];

    /// <summary>Every unique work name, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> WorkNames { get; } = [ReadBackWorkName, BodySyncWorkName];

    /// <summary>Every preference file name and key, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> PreferenceKeys { get; } = [SettingsPreferencesName, PillarWorkoutsKey, PillarMealsKey, PillarBodyKey];
}
