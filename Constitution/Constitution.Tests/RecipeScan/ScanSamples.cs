using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Tests.RecipeScan;

/// <summary>Invented card text for the scan tests: one made-up dish and the made-up doubts a judge might raise about it.</summary>
internal static class ScanSamples
{
    public static RecipeReadPayload AppleCrisp() => new(
        "Apple crisp",
        ["6 apples, peeled and sliced", "¾ cup brown sugar", "1 cup rolled oats", "½ cup butter, cold"],
        ["Oven to 350°F", "Bake 40 min, until bubbling"]);

    /// <summary>The three doubts p.14 draws: the oven, the sugar and the fat. The last has one alternative only.</summary>
    public static IReadOnlyList<UncertainWord> ThreeDoubts() =>
    [
        new UncertainWord(ScanLineKind.Step, 0, "Oven", "350°F", ["330°F", "380°F"]),
        new UncertainWord(ScanLineKind.Ingredient, 1, "Sugar", "¾ cup", ["¼ cup", "⅓ cup"]),
        new UncertainWord(ScanLineKind.Ingredient, 3, "Fat", "butter", ["batter"]),
    ];
}

internal sealed class FakeJudge(IReadOnlyList<UncertainWord> words) : IRecipeWordJudge
{
    public int Calls { get; private set; }

    public RecipeReadPayload? Asked { get; private set; }

    public IReadOnlyList<UncertainWord> Judge(RecipeReadPayload payload)
    {
        Calls++;
        Asked = payload;
        return words;
    }
}

internal sealed class RecordingKeeper : IRecipeKeeper
{
    public List<Recipe> Kept { get; } = [];

    public Exception? Throws { get; init; }

    public string? Refusal { get; init; }

    public RecipeKeepResult Keep(Recipe recipe)
    {
        if (Throws is not null)
        {
            throw Throws;
        }

        if (Refusal is not null)
        {
            return RecipeKeepResult.Refused(Refusal);
        }

        Kept.Add(recipe);
        return RecipeKeepResult.Done;
    }
}
