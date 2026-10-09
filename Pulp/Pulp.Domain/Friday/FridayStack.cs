using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Domain.Friday;

/// <summary>How many films and shows the week's stack holds (D106: configurable, default three and two).</summary>
public sealed record StackSizes
{
    public StackSizes(int films, int shows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(films);
        ArgumentOutOfRangeException.ThrowIfNegative(shows);
        Films = films;
        Shows = shows;
    }

    public static StackSizes Default { get; } = new(3, 2);

    public int Films { get; }

    public int Shows { get; }

    public int For(TitleKind kind) => kind switch
    {
        TitleKind.Film => Films,
        TitleKind.Show => Shows,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A title kind the stack has no pile for."),
    };
}

/// <summary>A title the stack could hold — anything, on the household's services or not (D107).</summary>
public sealed record StackCandidate(TitleId Title, TitleKind Kind, IReadOnlyList<string> Tags);

/// <summary>A card's state on the pile: untouched, added to a playlist, or struck by a veto (D220).</summary>
public enum CardState
{
    Fresh,
    Added,
    Struck,
}

/// <summary>One poster in a pile.</summary>
public sealed record StackCard(TitleId Title, CardState State);

/// <summary>
/// The week's stack (D22, D106–D112, D219–D221): two piles, films and shows (D108), sized from
/// Settings, taste first (D107), built for the week that starts at Friday 6 am (D110) and replaced
/// whole the next Friday — nothing carries over, and nothing refills it midweek (D112).
///
/// Whether a card is struck is read from the viewer's <see cref="VetoBook"/> rather than stored on
/// the stack, so the strike on the pile and the permanent veto can never disagree: a veto strikes
/// the card where it stands until Friday, and "Bring it back" lifts both at once.
/// </summary>
public sealed class FridayStack
{
    private readonly IReadOnlyList<TitleId> films;
    private readonly IReadOnlyList<TitleId> shows;
    private readonly IReadOnlySet<TitleId> added;

    private FridayStack(
        ViewerId viewer,
        DateTimeOffset turnover,
        DateTimeOffset until,
        IReadOnlyList<TitleId> films,
        IReadOnlyList<TitleId> shows,
        IReadOnlySet<TitleId> added)
    {
        Viewer = viewer;
        Turnover = turnover;
        Until = until;
        this.films = films;
        this.shows = shows;
        this.added = added;
    }

    public ViewerId Viewer { get; }

    /// <summary>The Friday 6 am this stack was built for.</summary>
    public DateTimeOffset Turnover { get; }

    /// <summary>The next Friday 6 am, when this stack is replaced.</summary>
    public DateTimeOffset Until { get; }

    /// <summary>
    /// Builds the stack for the week containing <paramref name="now"/>. Vetoed titles are left out
    /// (D111), and so is anything the viewer has already watched — the stack is always something new
    /// (D282); the rest are ranked by the viewer's taste, ties by title id so a rebuild is stable.
    /// A child gets an empty stack: no taste profile, so nothing to pick by (D23, D26). Whether the
    /// viewer is a child is the <paramref name="roster"/>'s answer; a viewer not on it is refused.
    /// </summary>
    public static FridayStack Build(
        ViewerId viewer,
        HouseholdRoster roster,
        IEnumerable<StackCandidate> candidates,
        TasteProfile taste,
        VetoBook vetoes,
        IReadOnlySet<TitleId> alreadyWatched,
        StackSizes sizes,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(taste);
        ArgumentNullException.ThrowIfNull(vetoes);
        ArgumentNullException.ThrowIfNull(alreadyWatched);
        ArgumentNullException.ThrowIfNull(sizes);

        var isChild = roster.IsChild(viewer);
        if (vetoes.Viewer != viewer)
        {
            throw new ArgumentException(
                $"The vetoes are {vetoes.Viewer}'s, not {viewer}'s; vetoes are per viewer (D111).", nameof(vetoes));
        }

        var turnover = FridayTurnover.Current(now, zone);
        var until = FridayTurnover.Next(now, zone);
        if (isChild)
        {
            return new FridayStack(viewer, turnover, until, [], [], new HashSet<TitleId>());
        }

        var ranked = candidates
            .Where(candidate => !vetoes.IsVetoed(candidate.Title) && !alreadyWatched.Contains(candidate.Title))
            .DistinctBy(candidate => candidate.Title)
            .OrderByDescending(candidate => taste.Affinity(candidate.Tags))
            .ThenBy(candidate => candidate.Title.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FridayStack(
            viewer,
            turnover,
            until,
            Pick(ranked, TitleKind.Film, sizes),
            Pick(ranked, TitleKind.Show, sizes),
            new HashSet<TitleId>());
    }

    /// <summary>A pile in its order, each card's state read against the viewer's vetoes.</summary>
    public IReadOnlyList<StackCard> Pile(TitleKind kind, VetoBook vetoes)
    {
        ArgumentNullException.ThrowIfNull(vetoes);
        return Titles(kind).Select(title => new StackCard(title, State(title, vetoes))).ToList();
    }

    /// <summary>"Add to…" took this card to a playlist (D109). The card stays on its pile.</summary>
    public FridayStack Add(TitleId title)
    {
        if (!films.Contains(title) && !shows.Contains(title))
        {
            throw new ArgumentException($"{title} is not in this week's stack.", nameof(title));
        }

        return new FridayStack(Viewer, Turnover, Until, films, shows, new HashSet<TitleId>(added) { title });
    }

    /// <summary>Every card added or struck: the voiced empty state until Friday, no refill (D112).</summary>
    public bool IsUsedUp(VetoBook vetoes)
    {
        ArgumentNullException.ThrowIfNull(vetoes);
        return films.Concat(shows).All(title => State(title, vetoes) != CardState.Fresh);
    }

    /// <summary>True from the next Friday 6 am: the stack is replaced whole.</summary>
    public bool IsDue(DateTimeOffset now) => now >= Until;

    private IReadOnlyList<TitleId> Titles(TitleKind kind) => kind switch
    {
        TitleKind.Film => films,
        TitleKind.Show => shows,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A title kind the stack has no pile for."),
    };

    private CardState State(TitleId title, VetoBook vetoes)
    {
        if (vetoes.Viewer != Viewer)
        {
            throw new ArgumentException($"The vetoes are {vetoes.Viewer}'s, not {Viewer}'s.", nameof(vetoes));
        }

        if (vetoes.IsVetoed(title))
        {
            return CardState.Struck;
        }

        return added.Contains(title) ? CardState.Added : CardState.Fresh;
    }

    private static List<TitleId> Pick(IEnumerable<StackCandidate> ranked, TitleKind kind, StackSizes sizes) =>
        ranked
            .Where(candidate => candidate.Kind == kind)
            .Take(sizes.For(kind))
            .Select(candidate => candidate.Title)
            .ToList();
}
