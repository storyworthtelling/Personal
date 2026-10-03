using Index.Domain.Household;

namespace Index.Android;

// The device-side home of InstallRoleService's remembered answer: the local settings, under the frozen
// "InstallRole" key. A thin adapter so the decision stays in Index.Domain, where it is tested.
public sealed class PreferencesInstallRoleCache : IInstallRoleCache
{
    public InstallRole? Get() => SettingsStore.CachedInstallRole;

    public void Set(InstallRole role) => SettingsStore.CachedInstallRole = role;

    public void Clear() => SettingsStore.CachedInstallRole = null;
}
