using Pulp.Domain.Core;

namespace Pulp.Domain.Library;

/// <summary>Where a child's favorite came from (D230); the poster's small line says which.</summary>
public enum FavoriteSource
{
    ChildStar,
    ParentPick,
    WatchedMost,
}

/// <summary>The child's own star on a title page.</summary>
public sealed record ChildStar(TitleId Title, DateTimeOffset At);

/// <summary>A parent's pick for this child (D252, D254).</summary>
public sealed record ParentPick(TitleId Title, ViewerId Parent, DateTimeOffset At);

/// <summary>How many times the child has watched a title, and when last.</summary>
public sealed record WatchTally(TitleId Title, int Times, DateTimeOffset LastAt);

public sealed record FavoriteCard(TitleId Title, FavoriteSource Source, ViewerId? PickedBy, DateTimeOffset Since);

/// <summary>
/// A child's Favorites (D230): the child's own stars, a parent's picks, and the
/// <c>watchedMostCount</c> titles the child has watched most. A title is one card, labelled by the
/// child's own star first, then a parent's pick, then watched-most. Nothing failing the child's
/// filter appears from any source, nor takes a watched-most place. Newest first.
/// </summary>
public static class Favorites
{
    public static IReadOnlyList<FavoriteCard> For(
        IEnumerable<ChildStar> stars,
        IEnumerable<ParentPick> picks,
        IEnumerable<WatchTally> tallies,
        int watchedMostCount,
        Func<TitleId, bool> passesFilter)
    {
        ArgumentNullException.ThrowIfNull(stars);
        ArgumentNullException.ThrowIfNull(picks);
        ArgumentNullException.ThrowIfNull(tallies);
        ArgumentNullException.ThrowIfNull(passesFilter);
        ArgumentOutOfRangeException.ThrowIfNegative(watchedMostCount);

        var cards = new Dictionary<TitleId, FavoriteCard>();

        foreach (var star in stars.OrderByDescending(s => s.At))
        {
            Offer(cards, new FavoriteCard(star.Title, FavoriteSource.ChildStar, PickedBy: null, star.At), passesFilter);
        }

        foreach (var pick in picks.OrderByDescending(p => p.At))
        {
            Offer(cards, new FavoriteCard(pick.Title, FavoriteSource.ParentPick, pick.Parent, pick.At), passesFilter);
        }

        var mostWatched = tallies
            .Where(t => t.Times > 0 && !cards.ContainsKey(t.Title) && passesFilter(t.Title))
            .GroupBy(t => t.Title)
            .Select(group => group.OrderByDescending(t => t.Times).ThenByDescending(t => t.LastAt).First())
            .OrderByDescending(t => t.Times)
            .ThenByDescending(t => t.LastAt)
            .ThenBy(t => t.Title.Value, StringComparer.OrdinalIgnoreCase)
            .Take(watchedMostCount);
        foreach (var tally in mostWatched)
        {
            cards[tally.Title] = new FavoriteCard(tally.Title, FavoriteSource.WatchedMost, PickedBy: null, tally.LastAt);
        }

        return cards.Values
            .OrderByDescending(card => card.Since)
            .ThenBy(card => card.Title.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Offer(Dictionary<TitleId, FavoriteCard> cards, FavoriteCard card, Func<TitleId, bool> passesFilter)
    {
        if (!cards.ContainsKey(card.Title) && passesFilter(card.Title))
        {
            cards[card.Title] = card;
        }
    }
}
