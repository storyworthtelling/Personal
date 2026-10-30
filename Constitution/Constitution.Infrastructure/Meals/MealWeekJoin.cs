using Constitution.Domain.Calendar;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Meals.Pipeline;

namespace Constitution.Infrastructure.Meals;

/// <summary>What the run is ready to do: ask (Ready), or stop with the typed reason (Stop).</summary>
public abstract record MealWeekPreparation
{
    private MealWeekPreparation()
    {
    }

    public sealed record Ready(MealWeekRequestInputs Inputs, IReadOnlyList<AssumedSetting> Assumed) : MealWeekPreparation;

    public sealed record Stop(MealWeekOutcome.NotPlanned Outcome) : MealWeekPreparation;
}

/// <summary>Joins the household's setup and the clock into the run's inputs. Pure.</summary>
public static class MealWeekJoin
{
    /// <summary>
    /// The week a run plans: the one after the week that holds now. It is a fixed function of the lease's week (the lease covers the week
    /// that contains now), so whoever holds this week plans the next one and nobody else does.
    /// </summary>
    public static DateOnly WeekFor(DateTimeOffset now, TimeZoneInfo zone) => PlanningWeek.StartOf(now, zone).AddDays(PlanningWeek.DaysInWeek);

    /// <summary>
    /// The inputs, or the typed reason to plan nothing: no household setup (not saved, or unreadable) is
    /// <see cref="MealNotPlannedBecause.NotConfigured"/> with the setup's own sentence, which quotes nothing the household wrote.
    /// </summary>
    public static MealWeekPreparation Prepare(HouseholdSetupRead setup, string? workScheduleJson, TimeZoneInfo phoneZone, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(phoneZone);

        if (setup is not HouseholdSetupRead.Configured configured)
        {
            return new MealWeekPreparation.Stop(
                new MealWeekOutcome.NotPlanned(MealNotPlannedBecause.NotConfigured, ((HouseholdSetupRead.NotSet)setup).Reason));
        }

        var made = SetupRules.From(configured.Setup, workScheduleJson, phoneZone);
        var onTheirClock = TimeZoneInfo.ConvertTime(now, made.Rules.Zone);
        return new MealWeekPreparation.Ready(new MealWeekRequestInputs(WeekFor(now, made.Rules.Zone), onTheirClock, made.Rules), made.Assumed);
    }
}
