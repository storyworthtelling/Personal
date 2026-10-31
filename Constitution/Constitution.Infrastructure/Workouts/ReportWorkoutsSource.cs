using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Infrastructure.Workouts;

/// <summary>What the week's request is made of now: the request, or the plain reason it cannot be made.</summary>
public abstract record WeekRequestRead
{
    private WeekRequestRead()
    {
    }

    public sealed record Ready(WorkoutPlanRequest Request) : WeekRequestRead;

    public sealed record Unavailable(string Reason) : WeekRequestRead;
}

/// <summary>The household's rules, equipment and busy time for a week, which the saved week does not hold. May be slow (calendars).</summary>
public interface IWeekRequests
{
    WeekRequestRead For(DateOnly weekStart, DateTimeOffset now);
}

/// <summary>
/// The Workouts tab's plan, read from the week the weekly run saved (<see cref="IWorkoutWeekReports"/>). No saved week is the honest
/// "no plan yet"; a saved week, the record of issued numbers or the skip ledger that is there and cannot be read is
/// <see cref="WorkoutsRead.Unavailable"/> with its own sentence, never an empty plan or an empty ledger. The issued numbers are read from the
/// store the run keeps, so the keys the tab names events by are the ones the run wrote. A swap is kept in the saved week; a skip is kept by the
/// ledger (<see cref="SettlingWorkoutsSource"/>) and the owner's moves (<see cref="OverridingWorkoutsSource"/>), so it adds nothing here.
/// </summary>
public sealed class ReportWorkoutsSource(
    IWorkoutWeekReports reports,
    IKeyAssignmentStore issued,
    ILedgerStore ledger,
    IWeekRequests requests,
    Func<DateOnly, bool> pagesDelivered,
    string? owner) : IWorkoutsSource
{
    public const string NoOwner = "No adult is marked as the owner of this phone yet, so there is no workout week to show.";

    public const string LedgerUnreadable = "The skip ledger could not be read, so the week is not shown.";

    public WorkoutsRead Read(DateTimeOffset now)
    {
        switch (reports.Load())
        {
            case WorkoutWeekRecordRead.Held held:
                return FromRecord(held.Week, now);
            case WorkoutWeekRecordRead.Unreadable unreadable:
                return new WorkoutsRead.Unavailable(unreadable.Reason);
            default:
                return new WorkoutsRead.NoPlanYet();
        }
    }

    public void Record(WorkoutsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change is not WorkoutsChange.Swapped swapped)
        {
            return;
        }

        switch (reports.Load())
        {
            case WorkoutWeekRecordRead.Held held:
                reports.Save(WeekRecordEdit.Apply(held.Week, swapped.Swap));
                break;
            case WorkoutWeekRecordRead.Unreadable unreadable:
                throw new InvalidOperationException(unreadable.Reason);
            default:
                throw new InvalidOperationException("There is no plan to change yet.");
        }
    }

    private WorkoutsRead FromRecord(StoredWorkoutWeek week, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return new WorkoutsRead.Unavailable(NoOwner);
        }

        var numbers = issued.Load();
        if (!numbers.IsReadable)
        {
            return new WorkoutsRead.Unavailable(numbers.UnreadableBecause!);
        }

        var skips = ledger.Load();
        if (skips.WholeFileUnreadable)
        {
            return new WorkoutsRead.Unavailable(LedgerUnreadable);
        }

        var asked = requests.For(week.WeekStart, now);
        return asked switch
        {
            WeekRequestRead.Ready ready => WeekRecordSnapshot.From(week, new WeekReadInputs(owner, now, ready.Request, numbers.Assignments, skips.Ledger, pagesDelivered)),
            WeekRequestRead.Unavailable unavailable => new WorkoutsRead.Unavailable(unavailable.Reason),
            _ => throw new InvalidOperationException("The week's request came back as neither made nor unavailable."),
        };
    }
}
