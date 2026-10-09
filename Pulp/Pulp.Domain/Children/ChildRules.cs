using Pulp.Domain.Core;
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
/// titles above it, the parents' favorites for the child (which allow-list too), and blocks by title,
/// franchise or whole service. A narrow input — the household file's own record maps onto it.
/// </summary>
public sealed record ChildRules(ViewerId Child, AgeBand Ceiling)
{
    public IReadOnlySet<TitleId> Allowed { get; init; } = new HashSet<TitleId>();

    public IReadOnlySet<TitleId> ParentFavorites { get; init; } = new HashSet<TitleId>();

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

    public bool Covers(StreamingService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.KidsOnly || attested.Contains(service.Id);
    }
}
