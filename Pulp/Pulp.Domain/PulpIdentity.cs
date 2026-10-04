namespace Pulp.Domain;

/// <summary>
/// The identifiers that must never move once Pulp has shipped a single build, in one place.
///
/// <para>Every one of these is invisible: nothing fails at build time if it changes, and nothing
/// fails loudly at run time either. The phone keeps the old app and never sees another update, a
/// worker stops running with no log the owner can reach, or the two installs stop finding each
/// other's household file. <c>Pulp.Tests/Guard/PulpInvariantsTests</c> is what fails instead.</para>
///
/// <para>It lives in Domain because Domain has no dependencies and every layer can see it,
/// including the desktop test build — which is what lets the guards run with no Android workload
/// installed. Java names are built from <see cref="ApplicationId"/> rather than typed out, so the
/// package id exists as exactly one literal.</para>
/// </summary>
public static class PulpIdentity
{
    /// <summary>
    /// Frozen from the first build. A device recognises an update only when the package name is
    /// unchanged, and the device-owner update channel installs by package id: rename it and the
    /// phone keeps the old Pulp installed and never sees another update.
    /// </summary>
    public const string ApplicationId = "com.storyworthtelling.pulp";

    /// <summary>
    /// The launcher activity's registered Java name. Without an explicit registration .NET for
    /// Android derives it from the assembly and namespace, so a later rename would move it and
    /// break the home-screen entry that points at it.
    /// </summary>
    public const string MainActivityName = ApplicationId + ".MainActivity";

    /// <summary>
    /// The Friday stack worker's registered Java name. WorkManager persists a scheduled row by
    /// class name and periodic work is enqueued with <c>Keep</c>, so a moved class is one
    /// WorkManager can no longer instantiate — and the Friday stack silently stops arriving.
    /// </summary>
    public const string FridayStackWorkerName = ApplicationId + ".FridayStackWorker";

    /// <summary>
    /// The Friday stack's unique work name, persisted beside the class name. Change it and the
    /// old schedule is never replaced, so the phone runs two schedules or none.
    /// </summary>
    public const string FridayStackWorkName = "pulp-friday-stack";

    /// <summary>The weekly availability refresh's registered Java name; frozen for the same reason as <see cref="FridayStackWorkerName"/>.</summary>
    public const string WeeklyRefreshWorkerName = ApplicationId + ".WeeklyRefreshWorker";

    /// <summary>The weekly refresh's unique work name; frozen for the same reason as <see cref="FridayStackWorkName"/>.</summary>
    public const string WeeklyRefreshWorkName = "pulp-weekly-refresh";

    /// <summary>The daily changes pass's registered Java name; frozen for the same reason as <see cref="FridayStackWorkerName"/>.</summary>
    public const string DailyChangesWorkerName = ApplicationId + ".DailyChangesWorker";

    /// <summary>The daily changes pass's unique work name; frozen for the same reason as <see cref="FridayStackWorkName"/>.</summary>
    public const string DailyChangesWorkName = "pulp-daily-changes";

    /// <summary>
    /// The shared household file in the owner's Drive. Both installs find it by this name on
    /// first discovery and by id after that; rename it and each install looks for a file the
    /// other never wrote, so the household quietly splits in two.
    /// </summary>
    public const string HouseholdFileName = "storyworthtelling-pulp-household.json";
}
