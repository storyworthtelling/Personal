using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;
using Pulp.Domain.Services;

namespace Pulp.Domain.Children;

/// <summary>A franchise or series, for a block that covers every title in it (D129).</summary>
public readonly record struct FranchiseId
{
    public FranchiseId(string value) =>
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A franchise id needs a value; an empty one would block nothing.", nameof(value))
            : value;

    public string Value { get; }

    public bool Equals(FranchiseId other) => StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value;
}

/// <summary>
/// One child's filter as the parents set it (D12, D127–D129, D255): a ceiling, an allow-list of
/// titles above it, the parents' picks for the child (which allow-list too), and blocks by title,
/// franchise or whole service. A narrow input — the household file's own record maps onto it.
/// The parents' picks are held once, as <see cref="ParentPick"/> records: the filter's
/// <see cref="ParentFavorites"/> and the child's Favorites both read them from here, so a pick above
/// the ceiling can never be on one and missing from the other (S4).
/// </summary>
public sealed record ChildRules(ViewerId Child, AgeBand Ceiling)
{
    private readonly IReadOnlyList<ParentPick> parentPicks = [];
    private readonly IReadOnlySet<TitleId> parentFavorites = new HashSet<TitleId>();

    public IReadOnlySet<TitleId> Allowed { get; init; } = new HashSet<TitleId>();

    /// <summary>A parent's picks for this child (D252, D254); each one allow-lists its title (D255).</summary>
    public IReadOnlyList<ParentPick> ParentPicks
    {
        get => parentPicks;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            parentPicks = value.ToList();
            parentFavorites = parentPicks.Select(pick => pick.Title).ToHashSet();
        }
    }

    /// <summary>The titles of <see cref="ParentPicks"/>; derived, never set on its own.</summary>
    public IReadOnlySet<TitleId> ParentFavorites => parentFavorites;

    public IReadOnlySet<TitleId> BlockedTitles { get; init; } = new HashSet<TitleId>();

    public IReadOnlySet<FranchiseId> BlockedFranchises { get; init; } = new HashSet<FranchiseId>();

    public IReadOnlySet<ServiceId> BlockedServices { get; init; } = new HashSet<ServiceId>();
}

/// <summary>What the filter needs to know about a title: its rating, franchises and homes.</summary>
public sealed record TitleFacts(TitleId Id, TitleKind Kind, ContentRating Rating)
{
    public IReadOnlyCollection<FranchiseId> Franchises { get; init; } = [];

    /// <summary>The services the title can be watched on; empty when not yet known.</summary>
    public IReadOnlyCollection<ServiceId> Homes { get; init; } = [];
}

/// <summary>
/// The services the owner has attested keep every adult profile PIN-locked (D210). Only on these —
/// or on a kids-only app — may a child start a title.
/// </summary>
public sealed class AttestedServices
{
    private readonly HashSet<ServiceId> attested;

    public AttestedServices(IEnumerable<ServiceId> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        attested = [.. services];
    }

    /// <summary>
    /// Whether a child may start titles on <paramref name="service"/>: its catalogue entry is a
    /// kids-only app, or the owner has attested it. The entry is looked up here, so whether it is
    /// kids-only comes from the catalogue alone; a service the catalogue does not know is not covered.
    /// </summary>
    public bool Covers(ServiceId service, ServiceCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var entry = catalogue.ById(service);
        return entry is not null && (entry.KidsOnly || attested.Contains(entry.Id));
    }
}
