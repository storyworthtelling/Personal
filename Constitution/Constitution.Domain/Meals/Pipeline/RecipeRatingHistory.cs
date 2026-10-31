using Constitution.Domain.Household;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// What each person last said of each dish, for the rating page's underline ("you circled Loved last time"). It is only a lookup: the taste profile that
/// it is read from is invisible and nothing outside the Taste files names it, so this is the whole of what the paper can learn of it.
/// </summary>
public sealed class LastRatings
{
    private readonly IReadOnlyDictionary<(PersonId Person, RecipeId Recipe), TasteVerdict> held;

    public LastRatings(IReadOnlyDictionary<(PersonId Person, RecipeId Recipe), TasteVerdict> held)
    {
        ArgumentNullException.ThrowIfNull(held);
        this.held = held;
    }

    /// <summary>Nobody has rated anything: every row of the page asks to be circled.</summary>
    public static LastRatings None { get; } = new(new Dictionary<(PersonId Person, RecipeId Recipe), TasteVerdict>());

    public TasteVerdict? Of(PersonId person, RecipeId recipe) => held.TryGetValue((person, recipe), out var verdict) ? verdict : null;
}
