using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The dish name for a card id, from the recipe box, read once when first asked. A box that cannot be read, or a card it no longer holds, has no name (the judge is then
/// told "none"); this never throws for a box that is merely unavailable.
/// </summary>
public sealed class TasteDishNames(IRecipeBoxSource box)
{
    private readonly Lazy<IReadOnlyDictionary<RecipeId, string>> titles = new(() => box.Read() is RecipeBoxRead.Ready ready
        ? ready.Snapshot.Cards.GroupBy(card => card.Id).ToDictionary(group => group.Key, group => group.First().Title)
        : new Dictionary<RecipeId, string>());

    public string? Of(RecipeId card) => titles.Value.TryGetValue(card, out var title) && title.Length > 0 ? title : null;
}
