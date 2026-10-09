using Pulp.Domain.Core;

namespace Pulp.Domain.Services;

/// <summary>
/// One streaming service as public catalogue data: Pulp's id, the Android package(s) its app ships
/// as, the availability source's id for it, and the merchant names its subscription charge appears
/// under (for Index, D90). Says nothing about whether any household subscribes.
/// A service cannot exist without at least one real package: a hand-off is always aimed at one
/// (finding §2.4, G8), so a service with nothing to aim at is refused here rather than at the door.
/// </summary>
public sealed record StreamingService
{
    public StreamingService(
        ServiceId id,
        string name,
        IReadOnlyList<string> packages,
        string sourceId,
        IReadOnlyList<string> merchantNames)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(merchantNames);
        if (packages.Count == 0 || packages.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                $"Service '{id}' needs at least one Android package, and none blank: a hand-off is always aimed at a package.",
                nameof(packages));
        }

        if (merchantNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"Service '{id}' has a blank merchant name.", nameof(merchantNames));
        }

        Id = id;
        Name = Require(name, nameof(name), id);
        Packages = packages.Select(package => package.Trim()).ToList();
        SourceId = Require(sourceId, nameof(sourceId), id);
        MerchantNames = merchantNames.Select(merchant => merchant.Trim()).ToList();
    }

    public ServiceId Id { get; }

    /// <summary>The service's display name.</summary>
    public string Name { get; }

    /// <summary>Every package the service's app ships as; the first is the one handed to.</summary>
    public IReadOnlyList<string> Packages { get; }

    /// <summary>The availability source's (Streaming Availability API) id for this service.</summary>
    public string SourceId { get; }

    /// <summary>Names a subscription charge appears under, matched whole and without case by Index.</summary>
    public IReadOnlyList<string> MerchantNames { get; }

    /// <summary>
    /// A kids-only app (finding §2.2): the app is the child's profile, so it needs no PIN-lock
    /// attestation before a child may start a title there.
    /// </summary>
    public bool KidsOnly { get; init; }

    /// <summary>The package a hand-off is aimed at.</summary>
    public string PrimaryPackage => Packages[0];

    private static string Require(string value, string what, ServiceId id) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Service '{id}' needs a {what}.", what)
            : value.Trim();
}
