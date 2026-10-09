using Pulp.Domain.Core;

namespace Pulp.Domain.Availability;

/// <summary>Where a title plays on one of the household's services, at what quality, and whether with ads.</summary>
public sealed record Home(ServiceId Service, VideoQuality Quality, bool WithAds);

/// <summary>
/// The home a title opens on, and the other homes listed beneath it (D216). No best home means the
/// title is not on the household's services, which is when the rent/buy line applies (D20, D53).
/// </summary>
public sealed record HomeChoice(Home? Best, IReadOnlyList<Home> Others)
{
    public bool OnOurServices => Best is not null;
}

/// <summary>
/// D28: of the household's services that carry a title, open on the best quality; D29: at equal
/// quality the ad-free one wins; D216: a home the viewer chose for this title wins while that
/// service still carries it.
/// </summary>
public static class BestHome
{
    public static HomeChoice Choose(TitleId title, IEnumerable<StreamOffer> offers, IEnumerable<ServicePlan> household, HomeOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(overrides);

        var plans = new Dictionary<ServiceId, ServicePlan>();
        foreach (var plan in household)
        {
            plans[plan.Service] = plan;
        }

        // One home per service: a service listing a title twice is one place to watch it, at its best.
        var homes = offers
            .Where(offer => plans.ContainsKey(offer.Service))
            .Select(offer => new Home(offer.Service, offer.Quality, offer.AlwaysWithAds || !plans[offer.Service].AdFree))
            .GroupBy(home => home.Service)
            .Select(group => group.OrderBy(home => home, Ranking).First())
            .OrderBy(home => home, Ranking)
            .ToList();

        if (homes.Count == 0)
        {
            return new HomeChoice(null, []);
        }

        var pinned = overrides.For(title);
        var best = homes.FirstOrDefault(home => pinned is not null && home.Service == pinned.Value) ?? homes[0];
        return new HomeChoice(best, homes.Where(home => home != best).ToList());
    }

    /// <summary>Best first: higher quality, then no ads, then the service id so the answer never depends on arrival order.</summary>
    private static readonly IComparer<Home> Ranking = Comparer<Home>.Create((a, b) =>
    {
        var byQuality = b.Quality.CompareTo(a.Quality);
        if (byQuality != 0)
        {
            return byQuality;
        }

        var byAds = a.WithAds.CompareTo(b.WithAds);
        return byAds != 0 ? byAds : StringComparer.OrdinalIgnoreCase.Compare(a.Service.Value, b.Service.Value);
    });
}
