using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Domain.Children;

/// <summary>Why the filter showed or hid a title, in the order the rules are asked.</summary>
public enum FilterReason
{
    Blocked,
    AllowListed,
    ParentFavorite,
    Unrated,
    WithinCeiling,
    AboveCeiling,
}

/// <summary>The filter's answer. <see cref="HiddenFor"/> names the child who hid it from a group.</summary>
public sealed record FilterVerdict(bool Visible, FilterReason Reason, ViewerId? HiddenFor = null);

/// <summary>Why a child may not start a title on a service.</summary>
public enum StartRefusal
{
    None,
    Hidden,
    NotAHome,
    ServiceBlocked,
    ServiceNotAttested,
    UnknownService,
}

public sealed record StartVerdict(StartRefusal Refusal, ViewerId? RefusedFor = null)
{
    public bool MayStart => Refusal == StartRefusal.None;
}

/// <summary>The one neutral badge a service row carries for a child (D249).</summary>
public enum ServiceBadge
{
    None,
    AdultsOnly,
}

/// <summary>
/// The children's filter (G9). The order is the rule, and every wrong order is silent — the title
/// just appears on a child's page:
/// <list type="number">
/// <item>a block — title, franchise, or every service the title is on — hides, whatever else (D12, D129);</item>
/// <item>an allow-list entry, a parent's favorite or a game in a league a parent allowed shows (D12, D255, D326);</item>
/// <item>an unrated title hides (D128);</item>
/// <item>the ceiling decides (D127).</item>
/// </list>
/// Several children together get the strictest answer (D175): a title is shown only if every one of
/// them would be shown it.
/// </summary>
public static class ChildFilter
{
    public static FilterVerdict Decide(ChildRules child, TitleFacts title)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(title);

        if (IsBlocked(child, title))
        {
            return Hidden(FilterReason.Blocked, child);
        }

        if (child.Allowed.Contains(title.Id))
        {
            return new FilterVerdict(true, FilterReason.AllowListed);
        }

        // A parent's league allowance is an explicit allow for every game in it (D317, D326): no rating is invented.
        if (title.League is { } league && child.AllowedLeagues.Contains(league))
        {
            return new FilterVerdict(true, FilterReason.AllowListed);
        }

        if (child.ParentFavorites.Contains(title.Id))
        {
            return new FilterVerdict(true, FilterReason.ParentFavorite);
        }

        if (!title.Rating.IsRated)
        {
            return Hidden(FilterReason.Unrated, child);
        }

        return AgeBands.Allows(child.Ceiling, title.Rating)
            ? new FilterVerdict(true, FilterReason.WithinCeiling)
            : Hidden(FilterReason.AboveCeiling, child);
    }

    /// <summary>The strictest child wins (D175): the first child, in any order, who would not see it.</summary>
    public static FilterVerdict DecideTogether(IReadOnlyCollection<ChildRules> children, TitleFacts title)
    {
        RequireChildren(children);
        FilterVerdict? shown = null;
        foreach (var child in children)
        {
            var verdict = Decide(child, title);
            if (!verdict.Visible)
            {
                return verdict;
            }

            shown ??= verdict;
        }

        return shown!;
    }

    /// <summary>
    /// Whether the children may start <paramref name="title"/> on <paramref name="service"/>: the title
    /// must be shown to all of them, the service must be one of its homes and blocked for none of them,
    /// and the service must be attested or a kids-only app (D210, D44). The service is named by id and
    /// read from <paramref name="catalogue"/>, so nothing a caller builds can claim to be kids-only.
    /// </summary>
    public static StartVerdict CanStart(
        IReadOnlyCollection<ChildRules> children,
        TitleFacts title,
        ServiceId service,
        ServiceCatalogue catalogue,
        AttestedServices attested)
    {
        RequireChildren(children);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(attested);

        var shown = DecideTogether(children, title);
        if (!shown.Visible)
        {
            return new StartVerdict(StartRefusal.Hidden, shown.HiddenFor);
        }

        if (!title.Homes.Contains(service))
        {
            return new StartVerdict(StartRefusal.NotAHome);
        }

        var blockedFor = children.FirstOrDefault(child => child.BlockedServices.Contains(service));
        if (blockedFor is not null)
        {
            return new StartVerdict(StartRefusal.ServiceBlocked, blockedFor.Child);
        }

        if (catalogue.ById(service) is null)
        {
            return new StartVerdict(StartRefusal.UnknownService);
        }

        return attested.Covers(service, catalogue)
            ? new StartVerdict(StartRefusal.None)
            : new StartVerdict(StartRefusal.ServiceNotAttested);
    }

    /// <summary>"Adults only" on a service no child may start titles on (D249), read from the catalogue.</summary>
    public static ServiceBadge BadgeFor(ServiceId service, ServiceCatalogue catalogue, AttestedServices attested)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(attested);
        return attested.Covers(service, catalogue) ? ServiceBadge.None : ServiceBadge.AdultsOnly;
    }

    private static bool IsBlocked(ChildRules child, TitleFacts title) =>
        child.BlockedTitles.Contains(title.Id)
        || title.Franchises.Any(child.BlockedFranchises.Contains)
        || (title.Homes.Count > 0 && title.Homes.All(child.BlockedServices.Contains));

    private static FilterVerdict Hidden(FilterReason reason, ChildRules child) => new(false, reason, child.Child);

    private static void RequireChildren(IReadOnlyCollection<ChildRules> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count == 0)
        {
            throw new ArgumentException(
                "A child's filter needs at least one child; with none it would have nobody to be strict for.",
                nameof(children));
        }
    }
}
