namespace Constitution.Domain.Diagnostics;

/// <summary>
/// Constitution's key actions (LOG-LOOP-PLAN.md §4.1, O10): the taps that start background work, declared to the
/// tap watch so they are never judged dead and their failure or silence is logged. Wire names in the logs,
/// listed by <c>KeyActionsDeclaredTests</c>, so a new one is a deliberate edit.
/// </summary>
public static class ConstitutionKeyActions
{
    /// <summary>Sign in with Google.</summary>
    public const string GoogleSignIn = "constitution.google-sign-in";

    /// <summary>Settings: saving the bed's login, on the phone and to the vault. The bed itself is signed in to by the
    /// body sync, whose answers are on the Sleep Number trail.</summary>
    public const string SleepNumberLogin = "constitution.sleep-number-login";

    /// <summary>Asking Health Connect for its permissions.</summary>
    public const string HealthConnectPermission = "constitution.health-connect-permission";

    /// <summary>Reading a recipe card through Plate.</summary>
    public const string ScanRecipe = "constitution.scan-recipe";

    /// <summary>Meals: Cook this soon, which plans a recipe into next week.</summary>
    public const string CookSoon = "constitution.cook-soon";

    /// <summary>Diagnostics: Send logs now.</summary>
    public const string SendLogsNow = "constitution.send-logs-now";

    public static IReadOnlyList<string> All { get; } =
        [GoogleSignIn, SleepNumberLogin, HealthConnectPermission, ScanRecipe, CookSoon, SendLogsNow];
}
