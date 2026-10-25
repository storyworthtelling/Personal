using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Tests.MealPlacement;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Groceries;

/// <summary>
/// The invented dishes and weeks the grocery tests stand on. Every card makes four servings, so a table of four takes its
/// quantities as written; the dishes are made up and so is every figure.
/// </summary>
internal static class GroceryData
{
    public static Recipe Card(string id, string title, int? servings, params Ingredient[] ingredients) =>
        new(new RecipeId(id), title, Cuisine.Other, ingredients, [new RecipeStep("Cook it.", 30, true)], servings, null, false);

    public static Ingredient Item(string? quantity, string? unit, string name) => new(quantity, unit, name);

    public static readonly Recipe Chili = Card(
        "bean-chili", "Bean chili", 4,
        Item("2", "cans", "black beans"), Item("1", null, "onion"), Item("1", "cup", "rice"),
        Item(null, null, "salt"), Item("1", "tbsp", "olive oil"));

    public static readonly Recipe Chicken = Card(
        "lemon-chicken", "Lemon chicken", 4,
        Item("1 1/2", "lb", "chicken thighs"), Item("1", "cup", "rice"), Item("2", null, "onions"),
        Item("1", null, "lemon"), Item(null, null, "salt"));

    public static readonly Recipe Pasta = Card(
        "tomato-pasta", "Tomato pasta", 4,
        Item("12", "oz", "spaghetti"), Item("1", "can", "crushed tomatoes"), Item("1", "tbsp", "olive oil"),
        Item("1/2", "cup", "parmesan"), Item("3", "cloves", "garlic"));

    public static readonly IReadOnlyList<Recipe> Cards = [Chili, Chicken, Pasta];

    /// <summary>Chili on Sunday, chicken on Monday, pasta on Tuesday; the rest of the week is nothing.</summary>
    public static MealWeek ThreeDinners() => MealPlacementData.WeekOf(
        MealPlacementData.CookedOn(0, Chili, Sam, 45),
        MealPlacementData.CookedOn(1, Chicken, Lee, 45),
        MealPlacementData.CookedOn(2, Pasta, Sam, 30));

    public static MealWeek Cooking(params (int Day, Recipe Card)[] dinners) =>
        MealPlacementData.WeekOf(dinners.Select(dinner => MealPlacementData.CookedOn(dinner.Day, dinner.Card, Sam, 45)).ToArray());

    public static Staple OnHand(string name, Aisle aisle, bool standing = false) => new(name, aisle, needMore: false, standing);

    public static Staple Out(string name, Aisle aisle) => new(name, aisle, needMore: true);

    public static GroceryLine Line(GroceryList list, string name) =>
        Assert.Single(list.Lines, line => string.Equals(line.Name, name, StringComparison.OrdinalIgnoreCase));
}
