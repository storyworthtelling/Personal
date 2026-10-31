using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Documents;

/// <summary>
/// One eater's row on the rating page (book p.9, D37): who, the dish that person ate (a child who ate the kids' meal
/// rates that dish, not the adults'), and the rating they last gave that dish, which the page pre-underlines.
/// </summary>
/// <param name="Person">The roster id the rating is recorded under.</param>
/// <param name="Name">The name as printed ("Sam"), short enough for the 80 point name column.</param>
/// <param name="Recipe">The card the dish is, so the rating lands on the dish that person ate.</param>
/// <param name="Dish">The dish as printed under the name.</param>
/// <param name="Last">This person's last rating of this dish, or null for the first time (the page asks them to circle).</param>
public sealed record RecipeRatingRow(PersonId Person, string Name, RecipeId Recipe, string Dish, TasteVerdict? Last);

/// <summary>What the last page of tonight's recipe needs: the cook, the day, and a row per eater.</summary>
public sealed record RecipeRatingModel(string Cook, DateTime Day, IReadOnlyList<RecipeRatingRow> Rows);
