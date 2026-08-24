namespace Storyworthtelling.Updates;

/// <summary>What an app is allowed to install when the update channel runs.</summary>
public enum UpdateRole
{
    /// <summary>
    /// The ordinary case: an app keeps itself current and touches nothing else.
    /// </summary>
    SelfOnly,

    /// <summary>
    /// The launcher, which additionally puts brand-new sibling apps on the device.
    /// It is the only app that can — a device with no copy of an app has nothing
    /// there to notice a release.
    /// </summary>
    Bootstrapper,
}

/// <summary>
/// Which of a release's APKs this particular app should install.
///
/// Every app used to be updated by the launcher, which made the launcher the single
/// point of failure for the whole suite's updates and meant a new app could not ship
/// without a launcher release. Now each app keeps itself current, and the launcher
/// keeps only the job nothing else can do: installing an app the device does not
/// have yet.
///
/// The rule that matters is the one that stops the two overlapping. A bootstrapper
/// does <b>not</b> update a sibling that is already installed, because that sibling
/// is already updating itself and two installers racing the same package is how a
/// half-applied update happens.
/// </summary>
public static class UpdateScope
{
    public static IReadOnlyList<InstallCandidate> Decide(
        UpdateRole role,
        string selfPackage,
        IEnumerable<InstallCandidate> available,
        IReadOnlyDictionary<string, long> installedVersionCodes)
    {
        var chosen = new List<InstallCandidate>();
        foreach (var candidate in available)
        {
            if (!SuiteApps.IsOwnApp(candidate.Package))
            {
                continue;
            }

            var isSelf = string.Equals(candidate.Package, selfPackage, StringComparison.Ordinal);
            var installed = installedVersionCodes.TryGetValue(candidate.Package, out var version)
                ? version
                : (long?)null;

            if (isSelf)
            {
                // An app updates itself when the release is ahead of what is running.
                if (installed is null || candidate.VersionCode > installed)
                {
                    chosen.Add(candidate);
                }

                continue;
            }

            // Someone else's app: install it only if it is missing entirely, and only
            // if this app is the one whose job that is.
            if (role == UpdateRole.Bootstrapper && installed is null)
            {
                chosen.Add(candidate);
            }
        }

        return chosen;
    }
}
