using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals;

/// <summary>One dinner that was cooked: the card and the evening. The planner's memory of the last weeks.</summary>
public sealed record CookedMeal(RecipeId Recipe, DateOnly Day);
