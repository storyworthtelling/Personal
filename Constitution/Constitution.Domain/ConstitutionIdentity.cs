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

    /// <summary>
    /// The connections the vault restores into this install, one string each, inside
    /// <see cref="SettingsPreferencesName"/>. Added with the Android vault services (C-4); the values
    /// are kept in app-private preferences the way the sibling heads keep theirs. Absent means empty.
    /// </summary>
    public const string SleepNumberLoginKey = "connection-sleep-number-login";

    /// <summary>The household Sleep Number password; see <see cref="SleepNumberLoginKey"/>.</summary>
    public const string SleepNumberPasswordKey = "connection-sleep-number-password";

    /// <summary>The reMarkable device token; see <see cref="SleepNumberLoginKey"/>.</summary>
    public const string RemarkableTokenKey = "connection-remarkable-token";

    /// <summary>The shared Claude key; see <see cref="SleepNumberLoginKey"/>.</summary>
    public const string ClaudeKeyKey = "connection-claude-key";

    /// <summary>The shared Gemini key; see <see cref="SleepNumberLoginKey"/>.</summary>
    public const string GeminiKeyKey = "connection-gemini-key";

    /// <summary>This person's Health Connect choices as JSON; see <see cref="SleepNumberLoginKey"/>.</summary>
    public const string HealthConnectChoicesKey = "connection-health-connect-choices";

    /// <summary>This person's work schedule as JSON, written by Foreword; see <see cref="SleepNumberLoginKey"/>.</summary>
    public const string WorkScheduleKey = "connection-work-schedule";

    /// <summary>
    /// Which equipment this install has ticked, one comma-joined string of the frozen ids in
    /// <c>EquipmentCatalogue</c> (a dash for none), inside <see cref="SettingsPreferencesName"/>. Absent means everything.
    /// </summary>
    public const string EquipmentOwnedKey = "equipment-owned";

    /// <summary>
    /// Whether the owner has explicitly confirmed the equipment checklist on first run (C-8.3.1), a
    /// boolean inside <see cref="SettingsPreferencesName"/>. Needed because <see cref="EquipmentOwnedKey"/>
    /// reads as everything ticked when never stored, so "never asked" cannot be told from "confirmed".
    /// </summary>
    public const string EquipmentConfirmedKey = "equipment-confirmed";

    /// <summary>Every registered worker Java name, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> Workers { get; } = [ReadBackWorkerName, BodySyncWorkerName];

    /// <summary>Every unique work name, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> WorkNames { get; } = [ReadBackWorkName, BodySyncWorkName];

    /// <summary>Every preference file name and key, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> PreferenceKeys { get; } = [
        SettingsPreferencesName, PillarWorkoutsKey, PillarMealsKey, PillarBodyKey,
        SleepNumberLoginKey, SleepNumberPasswordKey, RemarkableTokenKey, ClaudeKeyKey, GeminiKeyKey,
        HealthConnectChoicesKey, WorkScheduleKey, EquipmentOwnedKey,
        EquipmentConfirmedKey,
    ];
}
