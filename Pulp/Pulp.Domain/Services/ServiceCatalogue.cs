using Pulp.Domain.Core;

namespace Pulp.Domain.Services;

/// <summary>
/// The service table: id ↔ package(s) ↔ availability-source id ↔ merchant names, every key one-to-one
/// so a lookup can never answer with two services. Built from a list and checked on construction: a
/// package or a merchant claimed twice would route a hand-off, hide an app (D92) or count a charge
/// (D90) against the wrong service, silently.
/// </summary>
public sealed class ServiceCatalogue
{
    private readonly Dictionary<ServiceId, StreamingService> byId = [];
    private readonly Dictionary<string, StreamingService> byPackage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StreamingService> bySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StreamingService> byMerchant = new(StringComparer.OrdinalIgnoreCase);

    public ServiceCatalogue(IEnumerable<StreamingService> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var all = services.ToList();
        foreach (var service in all)
        {
            Claim(byId, service.Id, service, "service id");
            foreach (var package in service.Packages)
            {
                Claim(byPackage, package, service, "package");
            }

            Claim(bySource, service.SourceId, service, "availability-source id");
            foreach (var merchant in service.MerchantNames)
            {
                Claim(byMerchant, merchant, service, "merchant name");
            }
        }

        All = all;
    }

    /// <summary>
    /// The major US services, as public catalogue data (package names from each app's store listing,
    /// source ids from the Streaming Availability API's service list). Which of these a household
    /// subscribes to lives in the household file, never here. Not yet checked against a device or a
    /// live source response: a package that has moved breaks the hand-off and the hiding for that
    /// service, so the first device run checks every one.
    /// </summary>
    public static ServiceCatalogue Public { get; } = new(
    [
        new(new ServiceId("netflix"), "Netflix", ["com.netflix.mediaclient"], "netflix", ["Netflix", "Netflix.com"]),
        new(new ServiceId("disney-plus"), "Disney+", ["com.disney.disneyplus"], "disney", ["Disney+", "Disney Plus", "DisneyPlus"]),
        new(new ServiceId("hulu"), "Hulu", ["com.hulu.plus"], "hulu", ["Hulu", "Hulu.com"]),
        new(new ServiceId("hbo-max"), "HBO Max", ["com.wbd.stream", "com.hbo.hbonow"], "hbo", ["HBO Max", "Max.com", "HBOMax.com"]),
        new(new ServiceId("prime-video"), "Prime Video", ["com.amazon.avod.thirdpartyclient"], "prime", ["Prime Video", "Amazon Prime Video"]),
        new(new ServiceId("paramount-plus"), "Paramount+", ["com.cbs.app"], "paramount", ["Paramount+", "Paramount Plus"]),
        new(new ServiceId("peacock"), "Peacock", ["com.peacocktv.peacockandroid"], "peacock", ["Peacock", "PeacockTV"]),
    ]);

    public IReadOnlyList<StreamingService> All { get; }

    public StreamingService? ById(ServiceId id) => byId.GetValueOrDefault(id);

    public StreamingService? ByPackage(string? package) => Find(byPackage, package);

    public StreamingService? BySourceId(string? sourceId) => Find(bySource, sourceId);

    /// <summary>The service whose charge carries this merchant name, matched whole and without case.</summary>
    public StreamingService? ByMerchant(string? merchant) => Find(byMerchant, merchant);

    private static StreamingService? Find(Dictionary<string, StreamingService> index, string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : index.GetValueOrDefault(key.Trim());

    private static void Claim<TKey>(Dictionary<TKey, StreamingService> index, TKey key, StreamingService service, string what)
        where TKey : notnull
    {
        if (index.TryGetValue(key, out var holder))
        {
            throw new ArgumentException(
                $"The {what} '{key}' is claimed by both '{holder.Id}' and '{service.Id}'; every key names one service.",
                nameof(service));
        }

        index[key] = service;
    }
}
