using Android.Content;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Services;

namespace Constitution.WeeklyMeals;

/// <summary>
/// The meals' own trail (audit 08 §11). The meal pass runs every half hour and wrote into the 32 KB body.log, which it
/// rolled within days, burying the Body sync lines and making a meal failure look like a Body one. Its phrases are the
/// run's value-free ones (a step and an outcome, never a dish or a quantity).
/// </summary>
public sealed class MealsLog : ConstitutionTrail, IWorkoutWeekLog
{
    public const string FileName = "meals.log";

    /// <summary>The trail's cap; past it the older half is sealed into the log loop's outbox, not discarded.</summary>
    public const long MaxBytes = 64 * 1024;
    private const string Source = "Meals";

    public MealsLog(Context context)
        : base(context, FileName, MaxBytes, Source)
    {
    }
}
