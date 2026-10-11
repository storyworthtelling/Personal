using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Ratings;

namespace Pulp.Domain.Settings;

/// <summary>
/// Household → a child's rules (D12, D127–D129, D255): each edit the page makes, as a new value. The
/// filter's order is <see cref="ChildFilter"/>'s; these only change the lists it reads. The latest
/// choice is the parent's intent, so allowing a blocked title lifts its block and blocking an allowed
/// one takes it off the allow-list — the two never hold one title at once. The parents' picks ride
/// along untouched: they live in their own records (S4).
/// </summary>
public static class ChildRuleEdits
{
    // The order each rating system climbs in. TV-G sits below TV-Y7 because D281 admits it from the
    // youngest band ("suitable for all ages"), so the highest admitted is read off this order.
    private static readonly string[] FilmLadder = ["G", "PG", "PG-13", "R"];
    private static readonly string[] TvLadder = ["TV-Y", "TV-G", "TV-Y7", "TV-PG", "TV-14", "TV-MA"];

    /// <summary>A child added before a ceiling is chosen starts at the strictest band, never the loosest.</summary>
    public static ChildRules NewChild(ViewerId child) => new(child, AgeBand.UnderSeven);

    public static ChildRules Ceiling(ChildRules rules, AgeBand band)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with { Ceiling = band };
    }

    public static ChildRules Allow(ChildRules rules, TitleId title)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with { Allowed = With(rules.Allowed, title), BlockedTitles = Without(rules.BlockedTitles, title) };
    }

    public static ChildRules Unallow(ChildRules rules, TitleId title)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with { Allowed = Without(rules.Allowed, title) };
    }

    public static ChildRules Block(ChildRules rules, TitleId title)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with { BlockedTitles = With(rules.BlockedTitles, title), Allowed = Without(rules.Allowed, title) };
    }

    public static ChildRules Unblock(ChildRules rules, TitleId title)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with { BlockedTitles = Without(rules.BlockedTitles, title) };
    }

    public static ChildRules BlockService(ChildRules rules, ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with { BlockedServices = With(rules.BlockedServices, service) };
    }

    public static ChildRules UnblockService(ChildRules rules, ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with { BlockedServices = Without(rules.BlockedServices, service) };
    }

    public static ChildRules BlockFranchise(ChildRules rules, FranchiseId franchise)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with { BlockedFranchises = With(rules.BlockedFranchises, franchise) };
    }

    public static ChildRules UnblockFranchise(ChildRules rules, FranchiseId franchise)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with { BlockedFranchises = Without(rules.BlockedFranchises, franchise) };
    }

    /// <summary>A band by its ages: "Under 7", "7 to 9" … "17". No birthday is stored (D127).</summary>
    public static string BandLabel(AgeBand band) => band switch
    {
        AgeBand.UnderSeven => "Under " + (AgeBands.Oldest(band) + 1),
        AgeBand.Seventeen => AgeBands.Youngest(band).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => $"{AgeBands.Youngest(band)} to {AgeBands.Oldest(band)}",
    };

    /// <summary>The highest film and TV ratings the band admits, read from <see cref="AgeBands"/> so it cannot promise what the filter refuses.</summary>
    public static string BandDetail(AgeBand band)
    {
        var film = Highest(band, FilmLadder);
        var tv = Highest(band, TvLadder);
        return $"Up to {film} and {tv}";
    }

    private static string Highest(AgeBand band, string[] ladder) =>
        ladder.LastOrDefault(code => AgeBands.Allows(band, ContentRating.Parse(code)))
        ?? throw new InvalidOperationException($"The {band} band admits nothing on the ladder {string.Join(", ", ladder)}.");

    private static HashSet<T> With<T>(IReadOnlySet<T> set, T item) => [.. set, item];

    private static HashSet<T> Without<T>(IReadOnlySet<T> set, T item) => set.Where(entry => !EqualityComparer<T>.Default.Equals(entry, item)).ToHashSet();
}
