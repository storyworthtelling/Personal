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
}
