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
    /// The Health Connect permissions-rationale activity's registered Java name (C-7.1.1). Health
    /// Connect finds it by intent filter in the merged manifest, and the alias below targets it by
    /// this name, so a moved class leaves the permission request opening nothing, with no error.
    /// </summary>
    public const string HealthRationaleActivityName = ApplicationId + ".HealthRationaleActivity";

    /// <summary>
    /// The <c>activity-alias</c> Android 14 resolves <c>VIEW_PERMISSION_USAGE</c> through. It cannot be
    /// written as a .NET attribute, so it lives in <c>Properties/AndroidManifest.xml</c>, which repeats
    /// this name and its target as literals; <c>HealthManifestTests</c> checks the file against these.
    /// </summary>
    public const string PermissionUsageAliasName = ApplicationId + ".ViewPermissionUsageActivity";

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
    /// The daily workout-week worker's registered Java name (P13). Frozen for the same reason as <see cref="ReadBackWorkerName"/>:
    /// WorkManager persists the row by class name and the work is enqueued with <c>Keep</c>.
    /// </summary>
    public const string WorkoutWeekWorkerName = ApplicationId + ".WorkoutWeekWorker";

    /// <summary>The workout-week worker's unique work name; frozen for the same reason as <see cref="ReadBackWorkName"/>.</summary>
    public const string WorkoutWeekWorkName = "constitution-workout-week";

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
    /// When the Sleep Number login was last saved by hand here (UTC ticks as text), inside
    /// <see cref="SettingsPreferencesName"/>. A failure recorded before it describes a login that has
    /// since been replaced and must not shame the new one. Absent means never saved by hand.
    /// </summary>
    public const string SleepNumberSavedKey = "credential-sleep-number-saved-at";

    /// <summary>When the reMarkable was last paired here; see <see cref="SleepNumberSavedKey"/>.</summary>
    public const string RemarkableSavedKey = "credential-remarkable-saved-at";

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

    /// <summary>
    /// Which side of the Sleep Number bed this install's owner lies on, the word <c>left</c> or <c>right</c>, inside
    /// <see cref="SettingsPreferencesName"/> (C-7.1.2). The household login is shared, so the person is not: this is
    /// what tells the bed whose night to fetch. Absent means no side chosen, and then the watch is read alone.
    /// </summary>
    public const string SleeperSideKey = "sleeper-side";

    /// <summary>
    /// The budget receiver's registered Java name (PBI C-11.1.1). Index addresses it as an explicit component, so a moved class
    /// is a broadcast that reaches nothing, with no error on either phone. Frozen with the action and permission below.
    /// </summary>
    public const string BudgetReceiverName = ApplicationId + ".BudgetReceiver";

    /// <summary>The action Index sends the month's grocery budget under; frozen for the same reason as <see cref="BudgetReceiverName"/>.</summary>
    public const string BudgetAction = ApplicationId + ".action.BUDGET";

    /// <summary>
    /// The signature-level permission that protects <see cref="BudgetReceiverName"/>. Constitution declares it and Index
    /// holds it, so only an app signed with the suite's one keystore can send a budget. Not an Android runtime permission.
    /// </summary>
    public const string BudgetPermission = ApplicationId + ".RECEIVE_BUDGET";

    /// <summary>
    /// The household file's name in the owner's Drive (C-6.1.2): recipes, the week plan, ratings, the roster and
    /// the planning lease, sealed under the vault passphrase. It is found by name on first run, so a rename leaves
    /// the partner's phone looking for a file that is not there and the old one unread. Frozen; the kinds inside
    /// it are <c>ConstitutionKinds</c>.
    /// </summary>
    public const string HouseholdFileName = "storyworthtelling-constitution-household.json";

    /// <summary>Every registered activity and alias Java name, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> Activities { get; } = [MainActivityName, HealthRationaleActivityName, PermissionUsageAliasName];

    /// <summary>Every registered broadcast receiver Java name, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> Receivers { get; } = [BudgetReceiverName];

    /// <summary>Every custom permission this app declares, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> DeclaredPermissions { get; } = [BudgetPermission];

    /// <summary>Every registered worker Java name, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> Workers { get; } = [ReadBackWorkerName, BodySyncWorkerName, WorkoutWeekWorkerName];

    /// <summary>Every unique work name, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> WorkNames { get; } = [ReadBackWorkName, BodySyncWorkName, WorkoutWeekWorkName];

    /// <summary>Every preference file name and key, for the guard to enumerate.</summary>
    public static IReadOnlyList<string> PreferenceKeys { get; } = [
        SettingsPreferencesName, PillarWorkoutsKey, PillarMealsKey, PillarBodyKey,
        SleepNumberLoginKey, SleepNumberPasswordKey, RemarkableTokenKey, ClaudeKeyKey, GeminiKeyKey,
        HealthConnectChoicesKey, WorkScheduleKey, EquipmentOwnedKey,
        EquipmentConfirmedKey, SleepNumberSavedKey, RemarkableSavedKey, SleeperSideKey,
    ];
}
