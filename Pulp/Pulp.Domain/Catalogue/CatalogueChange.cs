using Pulp.Domain.Core;

namespace Pulp.Domain.Catalogue;

/// <summary>The kinds of catalogue change the availability source's changes feed reports (D204).</summary>
public enum ChangeKind
{
    New,
    Removed,
    Updated,
    Expiring,
    Upcoming,
}

/// <summary>What a change is about: a whole title, one season, or one episode.</summary>
public enum ChangeItem
{
    Title,
    Season,
    Episode,
}

/// <summary>
/// One change on one of the household's services. <see cref="At"/> is null for an expiring or
/// upcoming change whose date the service has not announced — "leaving soon" with no day (D124).
/// <see cref="Season"/> and <see cref="Episode"/> are set only for a season or episode change.
/// </summary>
public sealed record CatalogueChange(
    ChangeKind Kind,
    ChangeItem Item,
    TitleId Title,
    TitleKind TitleKind,
    ServiceId Service,
    OfferKind Offer,
    int? Season,
    int? Episode,
    DateTimeOffset? At,
    string? Link);

/// <summary>One page of the changes feed: the changes, the titles they touch, and where the next page starts.</summary>
public sealed record ChangesPage(
    IReadOnlyList<CatalogueChange> Changes,
    IReadOnlyDictionary<TitleId, CatalogueTitle> Titles,
    string? NextCursor)
{
    public bool HasMore => NextCursor is not null;
}
