using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Infrastructure.Meals;

/// <summary>Sends the meal author's origin line ("meal plan: rules only (not configured)", a fixed vocabulary) into the weekly run's trail.</summary>
public sealed class TrailMealLog(IWorkoutWeekLog log) : IMealLog
{
    private readonly IWorkoutWeekLog log = log ?? throw new ArgumentNullException(nameof(log));

    public void Record(PlanOrigin origin, AuthorUnavailable? reason) => log.Outcome(MealWeekTrail.LogPhrase(origin, reason));
}
