using Pulp.Domain.Availability;
using Pulp.Domain.Core;

namespace Pulp.Domain.Catalogue;

/// <summary>How a service offers a title, as the availability source names it.</summary>
public enum OfferKind
{
    /// <summary>Included in a paid subscription plan.</summary>
    Subscription,

    /// <summary>Free to watch, with ads whatever the plan.</summary>
    Free,

    /// <summary>Through a channel bought inside another service (a "Channels" add-on).</summary>
    Addon,

    Rent,

    Buy,
}

/// <summary>
/// One way to watch a title, an episode or a season on one service: the kind, the picture, the price
/// when it is paid, the two deep links (<see cref="Link"/> is always there; <see cref="VideoLink"/>
/// goes straight to the video when the source knows it), and the last day to watch when the service
/// has announced one. <see cref="Service"/> is the catalogue's id when the service is one Pulp knows
/// (<see cref="KnownService"/>), otherwise the source's own id — a store Pulp does not hand off to
/// can still be the rent/buy line.
/// </summary>
public sealed record TitleOffer(
    ServiceId Service,
    string ServiceName,
    bool KnownService,
    OfferKind Kind,
    VideoQuality Quality,
    decimal? Price,
    string? Currency,
    string Link,
    string? VideoLink,
    DateOnly? ExpiresOn,
    bool ExpiresSoon);

/// <summary>
/// Reads a title's offers into the shapes the existing availability rules take: the streams
/// <see cref="BestHome"/> chooses among, and the paid offers <see cref="RentLine"/> picks from.
/// </summary>
public static class TitleOffers
{
    /// <summary>The currency the rent/buy line is in; a price in any other is left out rather than compared.</summary>
    public const string LineCurrency = "USD";

    /// <summary>
    /// Subscription and free offers as <see cref="StreamOffer"/>s. An add-on is not a stream on the
    /// service it rides on: a channel bought inside one service is not that service's catalogue, so
    /// counting it would open a title on a service where the household cannot actually play it.
    /// </summary>
    public static IReadOnlyList<StreamOffer> Streams(IEnumerable<TitleOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return offers
            .Where(offer => offer.Kind is OfferKind.Subscription or OfferKind.Free)
            .Select(offer => new StreamOffer(offer.Service, offer.Quality, AlwaysWithAds: offer.Kind == OfferKind.Free))
            .ToList();
    }

    /// <summary>Rent and buy offers with a price in <see cref="LineCurrency"/>, as <see cref="PaidOffer"/>s.</summary>
    public static IReadOnlyList<PaidOffer> Paid(IEnumerable<TitleOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return offers
            .Where(offer => offer.Kind is OfferKind.Rent or OfferKind.Buy)
            .Where(offer => offer.Price is not null && string.Equals(offer.Currency, LineCurrency, StringComparison.OrdinalIgnoreCase))
            .Select(offer => new PaidOffer(offer.Service, offer.Kind == OfferKind.Rent ? PaidKind.Rent : PaidKind.Buy, offer.Quality, offer.Price!.Value))
            .ToList();
    }
}
