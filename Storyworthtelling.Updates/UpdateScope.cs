namespace Storyworthtelling.Updates;

/// <summary>What an app is allowed to install when the update channel runs.</summary>
public enum UpdateRole
{
    /// <summary>
    /// An app keeps itself current and touches nothing else. What every head does on
    /// an ordinary Android phone, where it can run the confirmed installer itself.
    /// </summary>
    SelfOnly,

    /// <summary>
    /// One app installs updates for the whole suite. What the launcher does on the
    /// locked phone, where it is the device owner and therefore the only thing that
    /// can install anything at all.
    /// </summary>
    Custodian,
}

/// <summary>
/// Which of a release's APKs this particular app should install.
///
/// The two roles exist because the answer depends on the device, not on the app. On
/// the locked phone the device-owner launcher is the only thing that can install
/// silently — a restriction the lockdown deliberately keeps — so it installs for
/// everyone and the cost of it being a single point of failure is accepted rather
/// than opened up. On an unrestricted phone every app can install itself, and does.
///
/// Both roles are gated on the owner namespace first, which is the part that is
/// never a device question: a package from anywhere else is not installed, whoever
/// put it on the release.
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

            var mine = string.Equals(candidate.Package, selfPackage, StringComparison.Ordinal);
            if (role == UpdateRole.SelfOnly && !mine)
            {
                continue;
            }

            // Not installed at all is what lets a brand-new sibling app land on its
            // first build, rather than waiting for someone to sideload it; newer is
            // the ordinary case. Older is never installed — the channel does not
            // downgrade, whatever a release happens to contain.
            if (!installedVersionCodes.TryGetValue(candidate.Package, out var installed)
                || candidate.VersionCode > installed)
            {
                chosen.Add(candidate);
            }
        }

        return chosen;
    }
}
