using Pulp.Domain.Core;

namespace Pulp.Domain.Availability;

public enum PaidKind
{
    Rent,
    Buy,
}

/// <summary>A store's price to rent or buy a title at a quality. Money is decimal; a negative price is refused.</summary>
public sealed record PaidOffer
{
    public PaidOffer(ServiceId store, PaidKind kind, VideoQuality quality, decimal price)
    {
        Store = store;
        Kind = kind;
        Quality = quality;
        Price = price < 0
            ? throw new ArgumentOutOfRangeException(nameof(price), price, "A price cannot be negative; it would always read as the cheapest line.")
            : price;
    }

    public ServiceId Store { get; }

    public PaidKind Kind { get; }

    public VideoQuality Quality { get; }

    public decimal Price { get; }
}

/// <summary>D53: the one rent/buy line — the cheapest offer at HD or better, or none at all.</summary>
public static class RentLine
{
    public static PaidOffer? Cheapest(IEnumerable<PaidOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);

        return offers
            .Where(offer => offer.Quality >= VideoQuality.Hd)
            .OrderBy(offer => offer.Price)
            .ThenByDescending(offer => offer.Quality)
            .ThenBy(offer => offer.Kind)
            .ThenBy(offer => offer.Store.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
