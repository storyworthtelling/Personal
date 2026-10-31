namespace Constitution.Domain.Recipes.Screens;

/// <summary>The two halves of the Meals tab (D54), turned between by the on-page switcher.</summary>
public enum MealsView
{
    ThisWeek,
    RecipeBox,
}

public static class MealsViews
{
    public static IReadOnlyList<MealsView> All { get; } = [MealsView.ThisWeek, MealsView.RecipeBox];

    /// <summary>The switcher's cells, in order (book p.13).</summary>
    public static IReadOnlyList<string> Labels { get; } = ["This week", "Recipe box"];
}
