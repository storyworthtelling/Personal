using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// What the planner is given of the taste profile: the cards a person has said never again to, added to the request's own, and the household's appetite for each
/// card as the ranking among cards that fit (D131). With no profile the request comes back as it was.
/// </summary>
public static class TasteForTheWeek
{
    public static MealWeekRequest Apply(MealTasteRead taste, MealWeekRequest request)
    {
        ArgumentNullException.ThrowIfNull(taste);
        ArgumentNullException.ThrowIfNull(request);
        return taste is MealTasteRead.Held held
            ? TastePlannerInputs.From(held.Profile, held.People, request.Recipes).Apply(request)
            : request;
    }

    /// <summary>What each person last said of each dish, for the rating page; nobody's when there is no profile.</summary>
    public static LastRatings LastRatings(MealTasteRead taste)
    {
        ArgumentNullException.ThrowIfNull(taste);
        return taste is MealTasteRead.Held held ? TasteLastRatings.From(held.Profile) : Constitution.Domain.Meals.Pipeline.LastRatings.None;
    }
}
