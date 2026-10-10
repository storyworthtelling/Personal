namespace Pulp.Domain.Actions;

/// <summary>Where a title was long-pressed — the card's actions change with it (mockup 10).</summary>
public enum CardPlace
{
    /// <summary>A row on Continue watching, the drawn case.</summary>
    ContinueWatching,

    /// <summary>A row on a playlist.</summary>
    PlaylistRow,

    /// <summary>A row on Recently watched.</summary>
    RecentlyWatched,

    /// <summary>An episode row on a title page (D160).</summary>
    Episode,

    /// <summary>A game on the Sports page. Not drawn; built from D55 and D100.</summary>
    SportsGame,
}

/// <summary>What a card entry does. The head wires each to its own handler.</summary>
public enum CardActionKind
{
    Play,
    MarkWatched,
    AddTo,
    RemoveFromContinueWatching,
    MoveToPlaylist,
    RemoveFromPlaylist,
    MarkUnwatched,
    WatchAgain,
    RemoveFromHistory,
    Watched,
    Unwatched,
    WatchedUpToHere,
    Favorite,
    Unfavorite,
}

/// <summary>
/// What was long-pressed. <see cref="NextUp"/> names what Play would play ("S1 · E3"), and
/// <see cref="NextEpisode"/> the episode a mark would mark ("E3"); both are null for a film or a game.
/// </summary>
public sealed record CardItem(string Title, CardPlace Place)
{
    /// <summary>The card header's second line, as the row described the item.</summary>
    public string? Subtitle { get; init; }

    public string? NextUp { get; init; }

    public string? NextEpisode { get; init; }

    /// <summary>The card is being opened in a child's view (D230).</summary>
    public bool ChildView { get; init; }

    /// <summary>In a child's view: the child has already starred this title.</summary>
    public bool ChildStarred { get; init; }

    /// <summary>For an episode: it is already watched, so the mark that changes it is Unwatched.</summary>
    public bool EpisodeWatched { get; init; }
}

/// <summary>
/// One line on the card. <see cref="UndoFirst"/> means the act happens at once and folds the undo
/// dog-ear (§13) — never a confirmation first.
/// </summary>
public sealed record CardEntry(CardActionKind Kind, string Label, bool UndoFirst);

public sealed record CardPlan(string Title, string? Subtitle, IReadOnlyList<CardEntry> Entries);

/// <summary>
/// The action card's contents (mockup 10, D256): item actions only, by where the title was pressed.
/// <list type="bullet">
/// <item>Continue watching: Play the next thing · Mark it watched (D160) · Add to… (D254) · Remove from Continue watching.</item>
/// <item>A playlist row: Play · Mark watched · Move to another playlist · Remove from this playlist.</item>
/// <item>Recently watched: Mark unwatched · Watch again · Remove from history.</item>
/// <item>An episode on a title page: the mark that changes it (Watched or Unwatched) · Watched up to here (D160).</item>
/// <item>A child's view: only Play and ★ Favorite (D230); the star is the child's request (D253).</item>
/// </list>
/// <b>Plan to watch is never here</b> (D251): plans are made from the title page only. Every removal and
/// every manual mark is undo-first (§13, D160).
/// </summary>
public static class ActionCardPlan
{
    public const string AddToLabel = "Add to…";
    public const string FavoriteLabel = "★ Favorite";

    public static CardPlan For(CardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Title))
        {
            throw new ArgumentException("An action card is headed by the title it acts on.", nameof(item));
        }

        var entries = item.ChildView ? ForChild(item) : ForAdult(item);
        return new CardPlan(item.Title.Trim(), Blank(item.Subtitle), entries);
    }

    private static IReadOnlyList<CardEntry> ForChild(CardItem item)
    {
        var play = new CardEntry(CardActionKind.Play, PlayLabel(item), UndoFirst: false);

        // A child's sport waits for the TV phase (D231), and favourites are of titles, not games.
        if (item.Place == CardPlace.SportsGame)
        {
            return [play];
        }

        var star = item.ChildStarred
            ? new CardEntry(CardActionKind.Unfavorite, "Remove from Favorites", UndoFirst: true)
            : new CardEntry(CardActionKind.Favorite, FavoriteLabel, UndoFirst: false);
        return [play, star];
    }

    private static IReadOnlyList<CardEntry> ForAdult(CardItem item) => item.Place switch
    {
        CardPlace.ContinueWatching =>
        [
            new(CardActionKind.Play, PlayLabel(item), false),
            new(CardActionKind.MarkWatched, MarkLabel(item), true),
            new(CardActionKind.AddTo, AddToLabel, false),
            new(CardActionKind.RemoveFromContinueWatching, "Remove from Continue watching", true),
        ],
        CardPlace.PlaylistRow =>
        [
            new(CardActionKind.Play, PlayLabel(item), false),
            new(CardActionKind.MarkWatched, MarkLabel(item), true),
            new(CardActionKind.MoveToPlaylist, "Move to another playlist", false),
            new(CardActionKind.RemoveFromPlaylist, "Remove from this playlist", true),
        ],
        CardPlace.RecentlyWatched =>
        [
            new(CardActionKind.MarkUnwatched, "Mark unwatched", true),
            new(CardActionKind.WatchAgain, "Watch again", false),
            new(CardActionKind.RemoveFromHistory, "Remove from history", true),
        ],
        CardPlace.Episode =>
        [
            item.EpisodeWatched
                ? new(CardActionKind.Unwatched, "Unwatched", true)
                : new(CardActionKind.Watched, "Watched", true),
            new(CardActionKind.WatchedUpToHere, "Watched up to here", true),
        ],
        CardPlace.SportsGame =>
        [
            new(CardActionKind.Play, PlayLabel(item), false),
            new(CardActionKind.MarkWatched, MarkLabel(item), true),
            new(CardActionKind.AddTo, AddToLabel, false),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.Place, "No action card is drawn for this place."),
    };

    private static string PlayLabel(CardItem item) =>
        Blank(item.NextUp) is { } next ? "Play " + next : "Play";

    private static string MarkLabel(CardItem item) =>
        Blank(item.NextEpisode) is { } episode ? $"Mark {episode} watched" : "Mark watched";

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
