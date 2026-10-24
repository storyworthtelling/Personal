using Constitution.Domain.Calendar;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Infrastructure.Workouts;

/// <summary>The files the weekly run keeps beside its other records, under the folder the head names.</summary>
public static class WorkoutWeekFiles
{
    public const string ReportFile = "week-report.json";
    public const string OverridesFile = "owner-moves.json";
}

/// <summary>What the run is ready to do: ask (Ready), or stop with the typed reason (Stop).</summary>
public abstract record WorkoutWeekPreparation
{
    private WorkoutWeekPreparation()
    {
    }

    public sealed record Ready(WorkoutWeekRequest Request, IReadOnlyList<AssumedSetting> Assumed) : WorkoutWeekPreparation;

    public sealed record Stop(WorkoutWeekOutcome.NotPlanned Outcome) : WorkoutWeekPreparation;
}

/// <summary>Joins the household's setup, the equipment checklist, the owner's moves and the clock into the run's request. Pure.</summary>
public static class WorkoutWeekJoin
{
    /// <summary>The Monday of the household's current week: the day on its own clock, then back to Monday. A DST change inside the week moves nothing, since days are dates.</summary>
    public static DateOnly MondayOf(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var day = ZoneClock.DayOf(now, zone);
        var sinceMonday = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-sinceMonday);
    }

    /// <summary>
    /// The request, or the typed reason to plan nothing: no household setup (not saved, or unreadable) is
    /// <see cref="NotPlannedBecause.NotConfigured"/> with the setup's own sentence, which quotes nothing the household wrote.
    /// </summary>
    public static WorkoutWeekPreparation Prepare(
        HouseholdSetupRead setup, string? workScheduleJson, TimeZoneInfo phoneZone, EquipmentSet equipment, PlanOverrides overrides, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(phoneZone);
        ArgumentNullException.ThrowIfNull(equipment);
        ArgumentNullException.ThrowIfNull(overrides);

        if (setup is not HouseholdSetupRead.Configured configured)
        {
            return new WorkoutWeekPreparation.Stop(
                new WorkoutWeekOutcome.NotPlanned(NotPlannedBecause.NotConfigured, [], ((HouseholdSetupRead.NotSet)setup).Reason));
        }

        var made = SetupRules.From(configured.Setup, workScheduleJson, phoneZone);
        var onTheirClock = TimeZoneInfo.ConvertTime(now, made.Rules.Zone);
        var request = new WorkoutWeekRequest(
            MondayOf(now, made.Rules.Zone),
            onTheirClock,
            made.Rules,
            equipment,
            configured.Setup.DeliverFor)
        {
            PlanOverrides = overrides,
        };
        return new WorkoutWeekPreparation.Ready(request, made.Assumed);
    }
}
