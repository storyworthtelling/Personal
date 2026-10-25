using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Documents;

/// <summary>The outlined word beside a dinner that is not an ordinary cooked one. Words, never colours (the rM2 is greyscale).</summary>
public enum MealMarker
{
    /// <summary>The cook starts it in the morning: the cook line says when.</summary>
    SlowCooker,

    /// <summary>A card the household has not cooked before.</summary>
    NewRecipe,
}

/// <summary>What the page prints for a cooked dinner's card: the dish's name, any marker, and the children's different meal.</summary>
public sealed record MealSheetDish(string Title, MealMarker? Marker = null, string? KidsMeal = null);

/// <summary>Where the family eats on an evening out and when they leave; the planned choice carries only a reason.</summary>
public sealed record MealSheetOuting(string Place, DateTime LeaveAt);

/// <summary>One approvable line of pages 2 and 3: who or what it is for ("Sam", "Kids", "Sandwich") and what is planned for it.</summary>
public sealed record MealSheetLine(string Label, string Text);

/// <summary>
/// What pages 2 and 3 of the sheet print (book p.7): the weekday breakfasts and lunches per person where they differ, the
/// weekend's one breakfast and one lunch for everyone (D31), and the children's lunchbox. The planner plans dinners only,
/// so the caller supplies these; every line is approved on its own (D30).
/// </summary>
public sealed record MealSheetDailyMeals(
    IReadOnlyList<MealSheetLine> WeekdayBreakfasts,
    IReadOnlyList<MealSheetLine> WeekdayLunches,
    string WeekendBreakfast,
    string WeekendLunch,
    MealSheetLunchbox Lunchbox);

/// <summary>The lunchbox's four parts for the children: a sandwich, a fruit and a snack in the box, then a separate snack of a fruit or veg and a bar.</summary>
public sealed record MealSheetLunchbox(string Sandwich, string Fruit, string Snack, string SeparateFruitOrVeg, string SeparateBar);

/// <summary>
/// What the weekly meal sheet needs (book p.7): the planned week, the dishes its cards stand for, and the
/// moment silence approves. <paramref name="Pages"/> is the sheet's length for "1 of 5"; <paramref name="Daily"/> adds pages 2 and 3
/// (breakfast and lunch, then lunchboxes); the later pages are built elsewhere.
/// </summary>
public sealed record MealSheetModel(
    MealWeek Week,
    IReadOnlyDictionary<RecipeId, MealSheetDish> Dishes,
    DateTimeOffset Deadline,
    IReadOnlyDictionary<DateOnly, MealSheetOuting>? Outings = null,
    string? Voice = null,
    bool VoiceIsOwners = true,
    int Pages = 5,
    MealSheetDailyMeals? Daily = null,
    MealSheetGroceries? Groceries = null,
    HolidayAsk? AskAhead = null);

/// <summary>
/// What the groceries page prints (book p.7): the week's list by store section, the standing staples to tick, and the budget in
/// words. The list is the preview only; the Keep note is the separate hand-off (D81, D90).
/// </summary>
public sealed record MealSheetGroceries(GroceryList List, Pantry Pantry, GroceryBudgetStatus Budget);
