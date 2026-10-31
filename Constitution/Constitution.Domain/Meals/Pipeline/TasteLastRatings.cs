using Constitution.Domain.Household;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>Reads the last verdicts out of the taste profile, which nothing outside the Taste files may name.</summary>
public static class TasteLastRatings
{
    public static LastRatings From(TasteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var held = new Dictionary<(PersonId Person, RecipeId Recipe), TasteVerdict>();
        foreach (var taste in profile.People)
        {
            foreach (var recipe in taste.NeverAgain)
            {
                held[(taste.Person, recipe)] = TasteVerdict.NeverAgain;
            }

            foreach (var recipe in taste.Fine)
            {
                held[(taste.Person, recipe)] = TasteVerdict.Fine;
            }

            foreach (var recipe in taste.Loved)
            {
                held[(taste.Person, recipe)] = TasteVerdict.Loved;
            }
        }

        return new LastRatings(held);
    }
}
