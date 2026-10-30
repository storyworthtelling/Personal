using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// What the planner can be given of the taste profile today: the cards a person has said never again to, added to the request's own.
/// The household's appetite for each card is not used: the request has no ranking input, and the planner is not changed here.
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
}
