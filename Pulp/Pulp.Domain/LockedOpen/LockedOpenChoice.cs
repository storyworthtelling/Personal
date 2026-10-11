using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Domain.LockedOpen;

/// <summary>How an allowed hand-off is carried out.</summary>
public enum HandOffRoute
{
    /// <summary>Today's hand-off: the service's own app, started with the title's link.</summary>
    Plain,

    /// <summary>Flyleaf starts the same link in lock task mode (D288).</summary>
    Locked,
}

/// <summary>
/// What Pulp knows about whether a locked hand-off can land on this phone: Flyleaf is installed
/// (visible to Pulp), and the packages Pulp told it to hide, which are the only ones it will lock.
/// </summary>
public sealed record LockedOpenFacts(bool FlyleafInstalled, IReadOnlySet<string> HiddenPackages)
{
    /// <summary>Facts for a household: its ticked services, by every package the catalogue knows for each.</summary>
    public static LockedOpenFacts For(bool flyleafInstalled, ServiceCatalogue catalogue, IEnumerable<ServiceId> ticked) =>
        new(
            flyleafInstalled,
            new HashSet<string>(Hidden.HiddenAppsHandOff.PackagesFor(catalogue, ticked), StringComparer.Ordinal));
}

/// <summary>
/// Chooses the route for a hand-off the planner has already allowed (D298). The locked one only where
/// it can land: Flyleaf is here and has been told this package. Anywhere else the plain hand-off
/// stands, so a broadcast nobody receives (her phone has no Flyleaf) is never why a title fails to open.
/// </summary>
public static class LockedOpenChoice
{
    public static HandOffRoute Decide(LockedOpenFacts? facts, string package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return facts is { FlyleafInstalled: true } && facts.HiddenPackages.Contains(package)
            ? HandOffRoute.Locked
            : HandOffRoute.Plain;
    }
}
