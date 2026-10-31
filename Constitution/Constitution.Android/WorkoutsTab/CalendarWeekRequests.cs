using Android.Content;
using Constitution.Domain.Calendar;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;
using Constitution.Services;
using Constitution.Settings;
using Constitution.WorkoutWeek;

namespace Constitution.WorkoutsTab;

/// <summary>
/// The week's request for the Workouts tab, made the way the weekly run makes it: the household's setup, the equipment checklist and each
/// adult's busy time read from the calendars, so a swap on the tab is held to the same rules and the same free time as the planner's.
/// A household that is not set up says so in the setup's own sentence. A calendar nobody could read is busy time that fails closed (the
/// tab then finds no room), not free time. Called off the UI thread, so the one calendar read waits where it stands.
/// </summary>
public sealed class CalendarWeekRequests(Context context, string folder, IHouseholdCalendarsSource calendars, ILedgerStore ledger) : IWeekRequests
{
    public WeekRequestRead For(DateOnly weekStart, DateTimeOffset now)
    {
        var workScheduleJson = new ConstitutionConnections(context).WorkScheduleJson;
        var prepared = WorkoutWeekJoin.Prepare(
            PlanningSetupFile.Read(Path.Combine(folder, WorkoutWeekCalendarSeams.SetupFile)),
            string.IsNullOrWhiteSpace(workScheduleJson) ? null : workScheduleJson,
            TimeZoneInfo.Local,
            new EquipmentStore(context).Read(),
            PlanOverrides.None,
            now);
        if (prepared is not WorkoutWeekPreparation.Ready ready)
        {
            return new WeekRequestRead.Unavailable(((WorkoutWeekPreparation.Stop)prepared).Outcome.Detail);
        }

        var rules = ready.Request.Rules;
        var zone = rules.Zone;
        var last = weekStart.AddDays(6);
        var snapshot = calendars.ReadAsync(ZoneClock.StartOfDay(weekStart, zone), ZoneClock.StartOfDay(weekStart.AddDays(7), zone), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var aware = new TripAwareCalendars(snapshot, rules);
        var skips = ledger.Load().Ledger;
        var people = rules.Adults
            .Select(adult => new PersonWeek(BusyTime.For(adult.Name, weekStart, last, rules, aware)) { Recovery = skips.RecoveryFor(adult.Name, weekStart) })
            .ToList();
        return new WeekRequestRead.Ready(
            new WorkoutPlanRequest(weekStart, rules, people, ready.Request.Equipment, ZoneClock.StartOfDay(weekStart, zone)) { Settings = ready.Request.Settings });
    }
}
