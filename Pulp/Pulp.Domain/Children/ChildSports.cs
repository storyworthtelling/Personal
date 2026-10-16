using Pulp.Domain.Core;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Children;

/// <summary>One league in a parent's editor: its name and whether this child may see it.</summary>
public sealed record ChildLeagueRow(LeagueId League, string Name, bool Allowed);

/// <summary>
/// What a child's Sports view may show (D59, D72, D317). Sport for a child is <b>allow-list only</b>:
/// a league is shown when a parent allowed it for that child and for no other reason, so a child with
/// nothing allowed sees nothing. Children together see the intersection of what each may (D255's
/// strictest-child reading). A held league keeps the spoiler hold exactly as an adult's does, and a
/// child is never given the one-tap peek; no league the child sees carries a highlight, since a
/// highlight's own title can read as a result (D314). Scores do not exist in these types (G10, G15).
///
/// <para>Whether the phone shows a Sports tab to a child is not decided here: D231 keeps it out of the
/// child's view, so this is the rule the TV phase draws from and the editor writes to.</para>
/// </summary>
public static class ChildSports
{
    /// <summary>The leagues parents have allowed this child.</summary>
    public static IReadOnlySet<LeagueId> AllowedFor(IEnumerable<Household.Records.ChildLeagueAllowance> allowances, ViewerId child)
    {
        ArgumentNullException.ThrowIfNull(allowances);
        return allowances.Where(entry => entry.Child == child).Select(entry => entry.League).ToHashSet();
    }

    /// <summary>
    /// The leagues every one of <paramref name="children"/> may see. No children is no leagues: an
    /// empty group is not a reason to open anything.
    /// </summary>
    public static IReadOnlySet<LeagueId> AllowedForAll(
        IEnumerable<Household.Records.ChildLeagueAllowance> allowances, IReadOnlyCollection<ViewerId> children)
    {
        ArgumentNullException.ThrowIfNull(allowances);
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count == 0)
        {
            return new HashSet<LeagueId>();
        }

        var all = allowances.ToList();
        return children
            .Select(child => AllowedFor(all, child))
            .Aggregate((common, next) => common.Intersect(next).ToHashSet());
    }

    public static bool Shows(IReadOnlySet<LeagueId> allowed, LeagueId league)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        return allowed.Contains(league);
    }

    /// <summary>
    /// The league as the child sees it, or null when it is not allowed. <paramref name="followed"/> and
    /// <paramref name="unwatchedRecordings"/> are the child's (or, for a group, everyone's); the hold is
    /// the adults' <see cref="SpoilerHold"/> with no peek.
    /// </summary>
    public static LeagueSheet? Present(
        LeagueFeed feed,
        IReadOnlySet<LeagueId> allowed,
        IReadOnlyCollection<TeamId> followed,
        IReadOnlyCollection<GameId> unwatchedRecordings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (!Shows(allowed, feed.League.Id))
        {
            return null;
        }

        var sheet = SpoilerHold.Present(feed, followed, unwatchedRecordings, now, peeking: false);
        return sheet is OpenLeague open ? open with { Highlights = [], IsPeek = false } : sheet;
    }

    /// <summary>Every league the household can read, in table order, ticked where this child may see it.</summary>
    public static IReadOnlyList<ChildLeagueRow> Rows(SportsSourceTable table, IReadOnlySet<LeagueId> allowed)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(allowed);
        return table.Leagues.Select(entry => new ChildLeagueRow(entry.League, entry.Name, allowed.Contains(entry.League))).ToList();
    }
}
