using Constitution.Domain.Household.Records;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Tests.Recipes;
using Storyworthtelling.Household;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>Invented cards and households for the recipe box screen tests: Sam, Lee, Robin and Juniper, made-up dishes.</summary>
internal static class BoxFixture
{
    public static readonly DateOnly Today = new(2026, 10, 24);

    public static Recipe Chili() => RecipeData.Chili();

    /// <summary>Twelve cards: the ten of <see cref="RecipeData.Box"/> plus two more.</summary>
    public static List<Recipe> Twelve()
    {
        var cards = RecipeData.Box();
        cards.Add(RecipeData.Plain("miso-salmon", "Miso salmon", Cuisine.Asian, "salmon", "miso"));
        cards.Add(RecipeData.Plain("turkey-chili-mac", "Turkey chili mac", Cuisine.American, "turkey", "macaroni"));
        return cards;
    }

    public static CardHistory ChiliHistory() => new(
        Today.AddDays(-14),
        6,
        "spring",
        [new CardReaction("Sam", Reaction.Loved), new CardReaction("Lee", Reaction.Loved), new CardReaction("Robin", Reaction.Loved), new CardReaction("Juniper", Reaction.Fine)]);

    public static CardHistory AllLoved() => new(
        Today.AddDays(-14),
        6,
        "spring",
        [new CardReaction("Sam", Reaction.Loved), new CardReaction("Lee", Reaction.Loved)]);
}

/// <summary>A source held in memory: cards in a household document, moves applied for real, and a planner that remembers.</summary>
internal sealed class InMemoryBoxSource : IRecipeBoxSource
{
    private HouseholdDocument document;
    private long tick = 5000;

    public InMemoryBoxSource(IEnumerable<Recipe> cards, IReadOnlyDictionary<RecipeId, CardHistory>? histories = null)
    {
        document = RecipeData.Document(cards);
        Histories = histories ?? new Dictionary<RecipeId, CardHistory>();
    }

    public IReadOnlyDictionary<RecipeId, CardHistory> Histories { get; }

    public HashSet<RecipeId> Planned { get; } = [];

    public List<RecipeId> Placements { get; } = [];

    public List<CuisineMove> Moves { get; } = [];

    public bool PlannerExists { get; set; } = true;

    public RecipeBoxRead Read() =>
        new RecipeBoxRead.Ready(new RecipeBoxSnapshot(HouseholdRecords.Read(document, RecipeMaps.Recipe).Items.ToList(), Histories, Planned));

    public void Move(CuisineMove move)
    {
        var result = move.Apply(document, RecipeData.At(tick++), RecipeData.OwnerWriter);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(result.Refusal);
        }

        document = result.Document;
        Moves.Add(move);
    }

    public CookSoonResult PlaceNextWeek(RecipeId card)
    {
        if (!PlannerExists)
        {
            return CookSoonResult.NoPlannerYet;
        }

        if (!Planned.Add(card))
        {
            return CookSoonResult.AlreadyPlaced;
        }

        Placements.Add(card);
        return CookSoonResult.Placed;
    }
}
