using FocusPhone.Domain.Policy;

namespace FocusPhone.Domain.Updates;

/// <summary>A release APK to consider installing: its package and versionCode.</summary>
public record InstallCandidate(string Package, long VersionCode);

/// <summary>
/// Decides which release APKs the device-owner update channel should install.
/// Pure so the rule is unit-tested away from Android. The channel carries only
/// the owner's own apps (namespace-gated, the same trust boundary the launcher
/// uses everywhere else); within that, an app is installed when it is newer than
/// what's on the device OR not installed at all — the latter is what lets a
/// brand-new sibling app (a future health app, say) land on its first CI build
/// without waiting out the 72-hour admin unlock.
/// </summary>
public static class SiblingUpdatePlan
{
    public static IReadOnlyList<InstallCandidate> Decide(
        IEnumerable<InstallCandidate> available,
        IReadOnlyDictionary<string, long> installedVersionCodes)
    {
        var toInstall = new List<InstallCandidate>();
        foreach (var candidate in available)
        {
            // Only the owner's own apps ride the channel — never a stray APK that
            // somehow reached the release.
            if (!candidate.Package.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (!installedVersionCodes.TryGetValue(candidate.Package, out var installed))
            {
                toInstall.Add(candidate); // not installed: a new app on first build
            }
            else if (candidate.VersionCode > installed)
            {
                toInstall.Add(candidate); // a newer build of an installed app
            }
        }
        return toInstall;
    }
}
