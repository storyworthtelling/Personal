using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.People;

namespace Pulp.Domain.Discover;

/// <summary>One poster on a pile, with what the page shows about it and its state (D220).</summary>
public sealed record DiscoverCard(DiscoverRow Row, CardState State);

/// <summary>
/// The week as Discover holds it: the <see cref="FridayStack"/> dealt once and kept, with each card's
/// row, until the next Friday 6 am (D110). It has to be kept rather than rebuilt on every open: a
/// rebuild leaves vetoed titles out (D111), so a card struck on Wednesday would vanish and the next
/// title would slide into its place — a refill, which D112 forbids — instead of staying struck where
/// it stands until Friday (D220).
///
/// <para>A card's state follows <see cref="FridayStack"/>'s own rule and reads the strike from the
/// viewer's <see cref="VetoBook"/> as it is now, so the pile and the permanent veto never disagree and
/// "Bring it back" lifts both at once. "Added" is remembered here, since a playlist add leaves the card
/// on its pile (D109).</para>
/// </summary>
public sealed class DiscoverPageWeek
{
    private readonly IReadOnlyList<DiscoverRow> films;
    private readonly IReadOnlyList<DiscoverRow> shows;
    private readonly IReadOnlySet<TitleId> added;

    internal DiscoverPageWeek(
        ViewerId viewer,
        DateTimeOffset turnover,
        DateTimeOffset until,
        IReadOnlyList<DiscoverRow> films,
        IReadOnlyList<DiscoverRow> shows,
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

    /// <summary>The Friday 6 am this week was dealt for.</summary>
    public DateTimeOffset Turnover { get; }

    /// <summary>The next Friday 6 am, when the week is dealt again.</summary>
    public DateTimeOffset Until { get; }

    /// <summary>Nothing was dealt — no candidates, or a child (D23). Not the same as used up.</summary>
    public bool IsEmpty => films.Count == 0 && shows.Count == 0;

    internal IReadOnlyList<DiscoverRow> Films => films;

    internal IReadOnlyList<DiscoverRow> Shows => shows;

    internal IReadOnlySet<TitleId> Added => added;

    /// <summary>
    /// Deals the week from <paramref name="pool"/> through <see cref="FridayStack.Build"/> — vetoed and
    /// already-watched titles left out (D111, D282), ranked by the viewer's own taste (D283) — and keeps
    /// each dealt title's row, its home read against <paramref name="household"/>.
    /// </summary>
    public static DiscoverPageWeek Deal(
        ViewerId viewer,
        HouseholdRoster roster,
        IEnumerable<CatalogueTitle> pool,
        TasteProfile taste,
        VetoBook vetoes,
        IReadOnlySet<TitleId> alreadyWatched,
        StackSizes sizes,
        DateTimeOffset now,
        TimeZoneInfo zone,
        IEnumerable<ServicePlan> household,
        HomeOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(overrides);

        var titles = pool.Where(title => title is not null).DistinctBy(title => title.Id).ToDictionary(title => title.Id);
        var candidates = titles.Values.Select(title => new StackCandidate(title.Id, title.Kind, title.Genres.ToList()));
        var stack = FridayStack.Build(viewer, roster, candidates, taste, vetoes, alreadyWatched, sizes, now, zone);
        var plans = household.ToList();

        List<DiscoverRow> RowsOf(TitleKind kind) =>
            stack.Pile(kind, vetoes).Select(card => DiscoverPageRows.For(titles[card.Title], plans, overrides)).ToList();

        return new DiscoverPageWeek(viewer, stack.Turnover, stack.Until, RowsOf(TitleKind.Film), RowsOf(TitleKind.Show), new HashSet<TitleId>());
    }

    /// <summary>A pile in its dealt order, each card's state read against the viewer's vetoes as they are now.</summary>
    public IReadOnlyList<DiscoverCard> Pile(TitleKind kind, VetoBook vetoes)
    {
        ArgumentNullException.ThrowIfNull(vetoes);
        if (vetoes.Viewer != Viewer)
        {
            throw new ArgumentException($"The vetoes are {vetoes.Viewer}'s, not {Viewer}'s; vetoes are per viewer (D111).", nameof(vetoes));
        }

        return Rows(kind).Select(row => new DiscoverCard(row, State(row.Id, vetoes))).ToList();
    }

    /// <summary>"Add to…" took this card to a playlist (D109). The card stays on its pile.</summary>
    public DiscoverPageWeek Add(TitleId title)
    {
        if (!films.Concat(shows).Any(row => row.Id == title))
        {
            throw new ArgumentException($"{title} is not in this week's stack.", nameof(title));
        }

        return new DiscoverPageWeek(Viewer, Turnover, Until, films, shows, new HashSet<TitleId>(added) { title });
    }

    /// <summary>Every card added or struck (D112): the empty state until Friday. An empty week is not used up.</summary>
    public bool IsUsedUp(VetoBook vetoes)
    {
        if (IsEmpty)
        {
            return false;
        }

        return Pile(TitleKind.Film, vetoes).Concat(Pile(TitleKind.Show, vetoes)).All(card => card.State != CardState.Fresh);
    }

    /// <summary>True from the next Friday 6 am: the week is dealt again, whole (D110).</summary>
    public bool IsDue(DateTimeOffset now) => now >= Until;

    private IReadOnlyList<DiscoverRow> Rows(TitleKind kind) => kind switch
    {
        TitleKind.Film => films,
        TitleKind.Show => shows,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A title kind the stack has no pile for."),
    };

    private CardState State(TitleId title, VetoBook vetoes)
    {
        if (vetoes.IsVetoed(title))
        {
            return CardState.Struck;
        }

        return added.Contains(title) ? CardState.Added : CardState.Fresh;
    }
}
