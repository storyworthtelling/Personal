using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.Core;

namespace Pulp.Domain.Discover;

/// <summary>
/// A catalogue title as the children's filter reads it: its rating, the services it streams on, and
/// the franchises it belongs to. The franchises are asked for rather than defaulted, because the
/// catalogue does not carry them and an empty default would quietly let a blocked franchise through.
/// </summary>
public static class DiscoverFacts
{
    public static TitleFacts For(CatalogueTitle title, Func<TitleId, IReadOnlyCollection<FranchiseId>> franchisesOf)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(franchisesOf);
        return new TitleFacts(title.Id, title.Kind, title.Rating)
        {
            Homes = TitleOffers.Streams(title.Offers).Select(offer => offer.Service).Distinct().ToList(),
            Franchises = franchisesOf(title.Id) ?? [],
        };
    }
}
