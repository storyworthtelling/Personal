using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Infrastructure.ReadBack;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Tests.WorkoutsTab;

internal sealed class FakeReports(WorkoutWeekRecordRead read) : IWorkoutWeekReports
{
    public WorkoutWeekRecordRead Read { get; set; } = read;

    public StoredWorkoutWeek? Saved { get; private set; }

    public WorkoutWeekRecordRead Load() => Read;

    public void Save(StoredWorkoutWeek week)
    {
        Saved = week;
        Read = new WorkoutWeekRecordRead.Held(week);
    }
}

internal sealed class FakeIssued(KeyAssignmentsRead read) : IKeyAssignmentStore
{
    public KeyAssignmentsRead Read { get; set; } = read;

    public KeyAssignmentsRead Load() => Read;

    public void Save(KeyAssignments assignments) => Read = KeyAssignmentsRead.Of(assignments);
}

internal sealed class FakeLedgerStore(SkipLedgerRead read) : ILedgerStore
{
    public SkipLedgerRead Read { get; set; } = read;

    public SkipLedgerRead Load() => Read;

    public void Save(SkipLedger ledger) => Read = new SkipLedgerRead(ledger, 0, false);
}

internal sealed class FakeRequests(WeekRequestRead answer) : IWeekRequests
{
    public WeekRequestRead Answer { get; set; } = answer;

    public int Asked { get; private set; }

    public WeekRequestRead For(DateOnly weekStart, DateTimeOffset now)
    {
        Asked++;
        return Answer;
    }
}
