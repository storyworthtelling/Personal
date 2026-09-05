namespace Codex.Domain.Policy;

/// <summary>
/// The "visibility IS runnability" rule (owner's policy directive): every installed, launchable app
/// that the <see cref="PolicyPlan"/> does not suspend must be actively un-suspended — not merely left
/// alone — so an app that a previous pass or the install watcher quarantined is released. Only the
/// never-visible set (games/social/dating/gambling/short-form + other browsers/launchers) and the
/// owner's explicit blocks stay suspended; everything else the owner can see, they can run.
/// Pure so the head stays a thin applier over tested logic.
/// </summary>
public static class PolicyReconciler
{
    /// <summary>The launchable packages to un-suspend: everything installed that the plan doesn't suspend.</summary>
    public static IReadOnlySet<string> PackagesToRelease(
        IEnumerable<string> installedLaunchable, IReadOnlySet<string> suspended)
    {
        var release = new HashSet<string>(installedLaunchable);
        release.ExceptWith(suspended);
        return release;
    }
}
