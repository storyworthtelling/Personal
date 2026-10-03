namespace Codex.Domain.Policy;

/// <summary>
/// String values of the android.os.UserManager restriction constants the
/// device owner applies. Kept in Domain so PolicyPlan composition is testable
/// without an Android dependency.
/// </summary>
public static class AndroidUserRestrictions
{
    public const string NoSafeBoot = "no_safe_boot";
    public const string NoUninstallApps = "no_uninstall_apps";
    public const string NoInstallUnknownSources = "no_install_unknown_sources";
    public const string NoFactoryReset = "no_factory_reset";
    public const string NoConfigDateTime = "no_config_date_time";
    public const string NoDebuggingFeatures = "no_debugging_features";
    public const string NoModifyAccounts = "no_modify_accounts";
    public const string NoControlApps = "no_control_apps";

    // Restrictions the owner's policy must never leave in force. Android keeps a restriction added by
    // AddUserRestriction until it is cleared, so an earlier build's setting outlives the plan that
    // dropped it and blocks the silent uninstall (DELETE_FAILED_USER_RESTRICTED).
    private static readonly string[] Retired = [NoUninstallApps, NoControlApps];

    public static IReadOnlyList<string> ToClear(IEnumerable<string> planned)
    {
        var keep = planned.ToHashSet();
        return Retired.Where(r => !keep.Contains(r)).ToArray();
    }
}
