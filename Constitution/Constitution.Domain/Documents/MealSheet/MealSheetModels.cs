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

/// <summary>
/// What page 1 of the weekly meal sheet needs (book p.7): the planned week, the dishes its cards stand for, and the
/// moment silence approves. <paramref name="Pages"/> is the sheet's length for "1 of 5"; the later pages are built elsewhere.
/// </summary>
public sealed record MealSheetModel(
    MealWeek Week,
    IReadOnlyDictionary<RecipeId, MealSheetDish> Dishes,
    DateTimeOffset Deadline,
    IReadOnlyDictionary<DateOnly, MealSheetOuting>? Outings = null,
    string? Voice = null,
    bool VoiceIsOwners = true,
    int Pages = 5);
