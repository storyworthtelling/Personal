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

    /// <summary>Signing in to the bed.</summary>
    public const string SleepNumberSignIn = "constitution.sleep-number-sign-in";

    /// <summary>Asking Health Connect for its permissions.</summary>
    public const string HealthConnectPermission = "constitution.health-connect-permission";

    /// <summary>Reading a recipe card through Plate.</summary>
    public const string ScanRecipe = "constitution.scan-recipe";

    /// <summary>Planning the week now.</summary>
    public const string Plan = "constitution.plan";

    /// <summary>Diagnostics: Send logs now.</summary>
    public const string SendLogsNow = "constitution.send-logs-now";

    public static IReadOnlyList<string> All { get; } =
        [GoogleSignIn, SleepNumberSignIn, HealthConnectPermission, ScanRecipe, Plan, SendLogsNow];
}
