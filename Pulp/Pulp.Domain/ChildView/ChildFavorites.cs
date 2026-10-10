using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Domain.ChildView;

/// <summary>One poster on a child's Favorites, and the child whose favorite it is.</summary>
public sealed record ChildFavoriteCard(FavoriteCard Card, ViewerId Child)
{
    public TitleId Title => Card.Title;
}

/// <summary>
/// A child's Favorites page as the shell builds it (mockup 06, D230): each child's own three sources
/// (<see cref="Favorites.For"/>, run against that child's filter), then only the titles every child
/// watching may see (D280), one poster a title, newest first. Also the poster's small line, and the
/// stars the child adds or takes away (D253) — stars are the child's own records, so starring together
/// stars it for each child watching.
/// </summary>
public static class ChildFavorites
{
    /// <summary>How many watched-most titles join a child's page. A number chosen, not measured.</summary>
    public const int WatchedMostCount = 3;

    public static IReadOnlyList<ChildFavoriteCard> For(
        ChildAudience audience,
        Func<ViewerId, IEnumerable<ChildStar>> starsOf,
        Func<ViewerId, IEnumerable<WatchEvent>> sittingsOf,
        Func<TitleId, TitleFacts?> factsFor,
        int watchedMostCount = WatchedMostCount)
    {
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentNullException.ThrowIfNull(starsOf);
        ArgumentNullException.ThrowIfNull(sittingsOf);
        ArgumentNullException.ThrowIfNull(factsFor);

        var cards = new Dictionary<TitleId, ChildFavoriteCard>();
        foreach (var child in audience.Children)
        {
            var own = Favorites.For(child, starsOf(child.Child), Tallies(sittingsOf(child.Child)), watchedMostCount, factsFor);
            foreach (var card in own.Where(card => audience.Shows(factsFor(card.Title))))
            {
                cards.TryAdd(card.Title, new ChildFavoriteCard(card, child.Child));
            }
        }

        return cards.Values
            .OrderByDescending(card => card.Card.Since)
            .ThenBy(card => card.Title.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>How often a child watched each title: every sitting but an "unwatched" mark; games are not favorites.</summary>
    public static IReadOnlyList<WatchTally> Tallies(IEnumerable<WatchEvent> sittings)
    {
        ArgumentNullException.ThrowIfNull(sittings);
        return sittings
            .Where(sitting => sitting.Kind != WatchedKind.Game && sitting.Evidence != WatchSource.MarkedUnwatched)
            .GroupBy(sitting => sitting.Title)
            .Select(group => new WatchTally(group.Key, group.Count(), group.Max(sitting => sitting.At)))
            .ToList();
    }

    /// <summary>The small line under a poster, for the parent reading over a shoulder (D230).</summary>
    public static string SourceLine(ChildFavoriteCard card, Func<ViewerId, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(nameOf);
        return card.Card.Source switch
        {
            FavoriteSource.ChildStar => $"★ {nameOf(card.Child)}'s star",
            FavoriteSource.ParentPick => card.Card.PickedBy is { } parent ? $"Picked by {nameOf(parent)}" : "Picked by a parent",
            FavoriteSource.WatchedMost => "Watched most",
            _ => throw new ArgumentOutOfRangeException(nameof(card), card.Card.Source, "A favorite from nowhere the page can name."),
        };
    }

    /// <summary>Whether any child watching has starred the title — the card then offers to take it away.</summary>
    public static bool IsStarred(ChildAudience audience, Func<ViewerId, IEnumerable<ChildStar>> starsOf, TitleId title) =>
        ToUnstar(audience, starsOf, title).Count > 0;

    /// <summary>The stars to write: one for each child watching who has not starred it yet.</summary>
    public static IReadOnlyList<ChildStarEntry> ToStar(
        ChildAudience audience, Func<ViewerId, IEnumerable<ChildStar>> starsOf, TitleId title, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentNullException.ThrowIfNull(starsOf);
        return audience.Watching
            .Where(child => !starsOf(child).Any(star => star.Title == title))
            .Select(child => new ChildStarEntry(child, new ChildStar(title, at)))
            .ToList();
    }

    /// <summary>The stars to take away: every watching child's star on the title, as recorded, so undo puts back the same ones.</summary>
    public static IReadOnlyList<ChildStarEntry> ToUnstar(ChildAudience audience, Func<ViewerId, IEnumerable<ChildStar>> starsOf, TitleId title)
    {
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentNullException.ThrowIfNull(starsOf);
        return audience.Watching
            .SelectMany(child => starsOf(child).Where(star => star.Title == title).Select(star => new ChildStarEntry(child, star)))
            .ToList();
    }
    /// <summary>
    /// The poster's where-line (mockup 06): the service it plays on for the child, and "free" when that
    /// home's offer costs nothing. No picture quality — a child picks by the picture, not the pixels.
    /// </summary>
    public static string WhereLine(CatalogueTitle title, Home home, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(home);
        var free = title.Offers.Any(offer => offer.Service == home.Service && offer.Kind == OfferKind.Free);
        return free ? $"On {serviceName} · free" : $"On {serviceName}";
    }
}
