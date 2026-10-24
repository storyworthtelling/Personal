namespace Constitution.Domain.Workouts.Pipeline;

/// <summary>What woke the weekly run.</summary>
public enum WorkoutWeekTrigger
{
    /// <summary>The daily worker, at its time.</summary>
    Scheduled,

    /// <summary>The app was opened (or the Workouts tab asked for the week).</summary>
    Opened,
}

/// <summary>
/// Whether a run is worth making now. The app opens many times a day and the Workouts tab asks for the week every time it is
/// shown; the run reads three calendars and the tablet, and (being idempotent) would find nothing to change, so an open shortly
/// after a run that planned the week reuses it. The scheduled run never waits on this, and a run that could not plan the week is never
/// reused: whoever has just signed in, shared a calendar or re-paired the tablet is opening the app to see it work.
/// </summary>
public static class WorkoutWeekDebounce
{
    /// <summary>How long a run that planned the week is good for when the app is opened.</summary>
    public static readonly TimeSpan OpenGap = TimeSpan.FromMinutes(10);

    /// <param name="lastPlanned">When the last run that planned the week finished, or null when there is none (never run, or the last one planned nothing).</param>
    public static bool ShouldRun(WorkoutWeekTrigger trigger, DateTimeOffset? lastPlanned, DateTimeOffset now)
    {
        if (trigger == WorkoutWeekTrigger.Scheduled || lastPlanned is not { } last)
        {
            return true;
        }

        // A last run ahead of now is a clock set back; it says nothing about how fresh the plan is.
        var age = now - last;
        return age < TimeSpan.Zero || age >= OpenGap;
    }
}
