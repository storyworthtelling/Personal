using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Domain.Workouts.Screens;
using Constitution.Tests.Placement;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// What the Workouts tab does when the owner swaps two days, played against the rig: the plan the tab shows, the change checked by the tab's own rules, the calendar made to follow.
/// The tab is handed the record of issued numbers the run keeps, as the source that fills its snapshot must: the keys it names events by are those numbers.
/// </summary>
internal static class OwnerHand
{
    public static WorkoutsSnapshot TabShows(WeekRig rig, WorkoutWeekReport report, DateOnly today, DateTimeOffset now)
    {
        var onCalendar = rig.Writer.Of(Sam).ToDictionary(
            item => DateOnly.FromDateTime(item.Start.DateTime),
            item => new DayFacts(OnCalendar: new PlacedTime(item.Start, item.End)));
        return new WorkoutsSnapshot(Sam, today, now, [new WorkoutWeek(report.Request, report.PlacedPlan)], onCalendar, SkipLedger.Empty)
        {
            Assignments = rig.KeyAssignments.Load().Assignments,
        };
    }

    public static async Task<(SwapResult Swap, PlanOverrides Overrides)> Swaps(WeekRig rig, WorkoutsSnapshot snapshot, DateOnly from, DateOnly to)
    {
        var check = SwapChoices.Check(snapshot, from, to);
        var swap = check.Result ?? throw new InvalidOperationException($"The tab would not allow it: {check.Reason}");
        var follow = new WorkoutCalendarFollow(rig.Writer, new CalendarEditor(rig.Writer), rig.Keys, rig.KeyAssignments);
        await follow.FollowSwapAsync(swap, CancellationToken.None);
        return (swap, PlanOverrides.From(new WorkoutsChange.Swapped(swap)));
    }

    private sealed class CalendarEditor(FakeCalendar calendar) : IWorkoutEventEditor
    {
        public Task<EventEdit> MoveAsync(string person, string eventId, PlacementKey newKey, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
        {
            calendar.Rekey(eventId, newKey, start, end);
            return Task.FromResult(EventEdit.Done());
        }

        public Task<EventEdit> RemoveAsync(string person, string eventId, CancellationToken cancellationToken)
        {
            calendar.Remove(eventId);
            return Task.FromResult(EventEdit.Done());
        }
    }
}
