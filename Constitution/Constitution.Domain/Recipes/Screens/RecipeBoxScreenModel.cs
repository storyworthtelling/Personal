namespace Constitution.Domain.Recipes.Screens;

/// <summary>What the recipe box half of Meals shows.</summary>
public enum BoxMode
{
    /// <summary>Ten cards or more: the piles, with search.</summary>
    Piles,

    /// <summary>Fewer than ten: the invitation, the counter and ten places (C-6.2.5).</summary>
    EmptyBox,
}

/// <summary>A card as the pile shows it: the card, and whether next week's plan holds it.</summary>
public sealed record BoxCard(Recipe Recipe, bool IsPlanned)
{
    public RecipeId Id => Recipe.Id;

    public string Title => Recipe.Title;
}

/// <summary>A pile under its label strip. <see cref="Pile"/> is what <see cref="PileLayout"/> places.</summary>
public sealed record PileView(Cuisine Cuisine, string Label, Pile Pile, IReadOnlyList<BoxCard> Cards);

/// <summary>The empty box: the state the domain decides, and which of the ten places hold which card.</summary>
public sealed record EmptyBoxView(EmptyBoxState State, IReadOnlyList<string?> PlaceTitles)
{
    /// <summary>Always true: the invitation is static copy, not one of the six voice seats, so nothing composes it.</summary>
    public bool InvitationIsStaticCopy => true;
}

/// <summary>
/// The recipe box half of the Meals tab as a pure model (C-6.2.1, C-6.2.5; book pp.13-14). Piles by cuisine, narrowed
/// in place by a search that sits under the switcher (D59); or, short of ten cards, the counting-to-ten page. It decides;
/// the view draws.
/// </summary>
public sealed class RecipeBoxScreenModel
{
    public const string NoMatchSentence = "No card in the box matches that.";

    private RecipeBoxScreenModel(
        BoxMode mode,
        string findLabel,
        IReadOnlyList<PileView> piles,
        string? nothingMatches,
        EmptyBoxView? empty,
        string primaryAction)
    {
        Mode = mode;
        FindLabel = findLabel;
        Piles = piles;
        NothingMatches = nothingMatches;
        Empty = empty;
        PrimaryAction = primaryAction;
    }

    public BoxMode Mode { get; }

    /// <summary>"Find a recipe · 23 cards": always the whole box, so a narrowed search still says how many there are.</summary>
    public string FindLabel { get; }

    public IReadOnlyList<PileView> Piles { get; }

    /// <summary>A sentence when a search found nothing, else null.</summary>
    public string? NothingMatches { get; }

    public EmptyBoxView? Empty { get; }

    /// <summary>The pinned bar's one action: Scan a recipe card while the box is short of ten, Add a recipe after.</summary>
    public string PrimaryAction { get; }

    /// <summary>The search field is the box's; the empty box has nothing to search.</summary>
    public bool ShowsSearch => Mode == BoxMode.Piles;

    public static RecipeBoxScreenModel Build(IReadOnlyList<Recipe> cards, string? query, IReadOnlySet<RecipeId>? plannedNextWeek = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var box = new RecipeBox(cards);
        var state = EmptyBox.For(box);
        var find = RecipeBoxText.FindLabel(box.Count);
        if (!state.PlanningMayStart)
        {
            var titles = box.Piles.SelectMany(pile => pile.Cards).Select(card => card.Title).OrderBy(title => title, StringComparer.CurrentCultureIgnoreCase).ToList();
            var places = Enumerable.Range(0, EmptyBox.Target).Select(place => place < titles.Count ? titles[place] : null).ToList();
            return new RecipeBoxScreenModel(BoxMode.EmptyBox, find, [], null, new EmptyBoxView(state, places), state.PrimaryAction);
        }

        var planned = plannedNextWeek ?? new HashSet<RecipeId>();
        var piles = box.Search(query)
            .Select(pile => new PileView(pile.Cuisine, Cuisines.Label(pile.Cuisine), pile, pile.Cards.Select(card => new BoxCard(card, planned.Contains(card.Id))).ToList()))
            .ToList();
        return new RecipeBoxScreenModel(BoxMode.Piles, find, piles, piles.Count == 0 ? NoMatchSentence : null, null, state.PrimaryAction);
    }
}
