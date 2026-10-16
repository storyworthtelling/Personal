using Pulp.Domain.Core;

namespace Pulp.Domain.Services;

/// <summary>One broadcaster name, as a sports feed spells it, and the catalogue service whose own app carries it.</summary>
public sealed record BroadcasterEntry(string Name, ServiceId Service);

/// <summary>What a game's channel text came to (D310).</summary>
public enum BroadcastKind
{
    /// <summary>No name in the channel text is in the table.</summary>
    Unknown,

    /// <summary>A name is in the table, but none of its services is one the household ticked.</summary>
    NotTicked,

    /// <summary>A name is in the table and its service is ticked.</summary>
    Ticked,
}

/// <summary>The resolution: its kind, and the service (null only when <see cref="Kind"/> is <see cref="BroadcastKind.Unknown"/>).</summary>
public sealed record BroadcastResolution(BroadcastKind Kind, ServiceId? Service);

/// <summary>
/// The broadcaster-name to service table (D310), kept beside <see cref="ServiceCatalogue"/> and printed in
/// Settings → About like the league to source table (D140). A sports feed names where a game is shown ("NBC ·
/// Peacock", "Prime Video"); this says which of the household's streaming apps carries that name in its own
/// app, so Play can open it through the one hand-off door. Names are matched whole and without case, so a
/// fragment ("Max" inside "Maximum") never resolves. Built from a list and checked on construction: a name
/// claimed twice, or a service the catalogue does not hold, would open the wrong app silently.
/// </summary>
public sealed class BroadcasterTable
{
    private static readonly char[] Separators = ['·', ',', '/', ';', '&'];

    private readonly Dictionary<string, BroadcasterEntry> byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ServiceCatalogue catalogue;

    public BroadcasterTable(ServiceCatalogue catalogue, IEnumerable<BroadcasterEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(entries);
        this.catalogue = catalogue;
        var all = new List<BroadcasterEntry>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                throw new ArgumentException("A broadcaster entry needs a name.", nameof(entries));
            }

            if (catalogue.ById(entry.Service) is null)
            {
                throw new ArgumentException($"The broadcaster '{entry.Name}' names the service '{entry.Service}', which the catalogue does not hold.", nameof(entries));
            }

            var clean = entry with { Name = entry.Name.Trim() };
            if (!byName.TryAdd(clean.Name, clean))
            {
                throw new ArgumentException($"The broadcaster '{clean.Name}' is listed twice; a name opens one service.", nameof(entries));
            }

            all.Add(clean);
        }

        Entries = all;
    }

    /// <summary>
    /// Real public broadcaster and streaming names, each mapped to the catalogue service whose own app streams it.
    /// Rights change and the table is data: a name the owner's phone does not find in the app is the first thing to
    /// check on a device. A linear network is listed only where the service streams it live in its own app (CBS in
    /// Paramount+, NBC in Peacock, the TNT group in HBO Max).
    /// </summary>
    public static BroadcasterTable Public { get; } = new(ServiceCatalogue.Public,
    [
        new("Peacock", new ServiceId("peacock")),
        new("PeacockTV", new ServiceId("peacock")),
        new("NBC", new ServiceId("peacock")),
        new("Prime Video", new ServiceId("prime-video")),
        new("Amazon Prime Video", new ServiceId("prime-video")),
        new("Amazon Prime", new ServiceId("prime-video")),
        new("Netflix", new ServiceId("netflix")),
        new("Paramount+", new ServiceId("paramount-plus")),
        new("Paramount Plus", new ServiceId("paramount-plus")),
        new("CBS", new ServiceId("paramount-plus")),
        new("ESPN+", new ServiceId("disney-plus")),
        new("Disney+", new ServiceId("disney-plus")),
        new("HBO Max", new ServiceId("hbo-max")),
        new("Max", new ServiceId("hbo-max")),
        new("TNT", new ServiceId("hbo-max")),
        new("TBS", new ServiceId("hbo-max")),
        new("truTV", new ServiceId("hbo-max")),
        new("TNT Sports", new ServiceId("hbo-max")),
        new("Hulu", new ServiceId("hulu")),
        new("Apple TV", new ServiceId("apple-tv")),
        new("Apple TV+", new ServiceId("apple-tv")),
    ]);

    public IReadOnlyList<BroadcasterEntry> Entries { get; }

    /// <summary>
    /// Resolves a game's channel text against the services the household ticked. A text naming several
    /// broadcasters is read in the order it names them; the first whose service is ticked wins, and when none is
    /// ticked the first known one is named so the card can say which to tick.
    /// </summary>
    public BroadcastResolution Resolve(string? channel, IReadOnlyCollection<ServiceId> ticked)
    {
        ArgumentNullException.ThrowIfNull(ticked);
        if (string.IsNullOrWhiteSpace(channel))
        {
            return new BroadcastResolution(BroadcastKind.Unknown, null);
        }

        var candidates = channel
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => byName.ContainsKey(part))
            .Select(part => byName[part].Service)
            .ToList();
        if (candidates.Count == 0)
        {
            return new BroadcastResolution(BroadcastKind.Unknown, null);
        }

        var open = candidates.Cast<ServiceId?>().FirstOrDefault(candidate => ticked.Contains(candidate!.Value));
        return open is { } found
            ? new BroadcastResolution(BroadcastKind.Ticked, found)
            : new BroadcastResolution(BroadcastKind.NotTicked, candidates[0]);
    }

    /// <summary>
    /// The service's own home as an https link on its own domain (the first of <see cref="StreamingService.LinkDomains"/>):
    /// the link-less open a game uses when no feed gives a deep link. It passes the same gates as a title link,
    /// including Flyleaf's own-domain check. Null when the catalogue records no domain for the service.
    /// </summary>
    public static Uri? HomeLink(StreamingService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service.LinkDomains.Count == 0)
        {
            return null;
        }

        // A domain that is already a subdomain (tv.apple.com) is the home as it stands; "www." goes only on a bare domain.
        var domain = service.LinkDomains[0];
        return new Uri(domain.Count(character => character == '.') > 1 ? "https://" + domain + "/" : "https://www." + domain + "/");
    }

    /// <summary>One line per service for About: "NBC, Peacock, PeacockTV open in Peacock".</summary>
    public IReadOnlyList<string> AboutLines() =>
        Entries
            .GroupBy(entry => entry.Service)
            .Select(group => $"{string.Join(", ", group.Select(entry => entry.Name).Order(StringComparer.OrdinalIgnoreCase))} open in {catalogue.ById(group.Key)!.Name}")
            .ToList();
}
