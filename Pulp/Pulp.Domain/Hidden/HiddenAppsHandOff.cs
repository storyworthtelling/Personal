using System.Text.Json;
using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Domain.Hidden;

/// <summary>
/// What Pulp tells Flyleaf to keep out of its tray (D8, D92): the package ids of exactly the services
/// the household ticked here, and only those. The wire names below are Flyleaf's own, copied
/// byte-for-byte from <c>FocusPhone/Codex.Domain/Apps/HiddenApps.cs</c> (<c>HiddenAppsHandoff</c>);
/// <c>HiddenAppsContractTests</c> (guard G17) reads that file by content, so a drift on either side
/// fails there instead of dropping the broadcast silently.
///
/// The send is an explicit broadcast to Flyleaf's named receiver under its signature permission, with
/// the set as one exact-text extra (a JSON array of package ids). The empty set is sent too: it is how
/// unticking the last service brings its app back. No outbox and no fallback (D93): if Flyleaf is
/// absent the send is dropped, and if Pulp is later missing Flyleaf keeps the last set it was given.
/// </summary>
public static class HiddenAppsHandOff
{
    public const string Package = "com.storyworthtelling.focusphone.launcher";

    public const string Receiver = "com.storyworthtelling.focusphone.launcher.HiddenAppsReceiver";

    public const string Action = "com.storyworthtelling.focusphone.launcher.action.HIDDEN_APPS";

    public const string Permission = "com.storyworthtelling.focusphone.launcher.RECEIVE_HIDDEN";

    public const string PackagesExtra = "packages";

    /// <summary>
    /// Every package of every ticked service the catalogue knows, once each, in a fixed order. A
    /// service ships as more than one package sometimes; hiding only the first would leave the other
    /// in the tray. An id the catalogue does not know hides nothing.
    /// </summary>
    public static IReadOnlyList<string> PackagesFor(ServiceCatalogue catalogue, IEnumerable<ServiceId> ticked)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(ticked);
        return ticked
            .Select(catalogue.ById)
            .OfType<StreamingService>()
            .SelectMany(service => service.Packages)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(package => package, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The payload as intent extras: one key, the packages as exact JSON text.</summary>
    public static IReadOnlyDictionary<string, string> Extras(IReadOnlyList<string> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PackagesExtra] = JsonSerializer.Serialize(packages),
        };
    }
}
