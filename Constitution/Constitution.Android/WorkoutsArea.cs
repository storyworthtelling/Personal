using Android.App;
using Android.OS;
using Android.Views;
using Constitution.Domain.Notices;
using Constitution.Domain.Workouts.Screens;
using Constitution.Settings;
using Constitution.WorkoutsTab;

namespace Constitution;

/// <summary>
/// The Workouts tab (C-5.4, book page 12). The area owns what the tab shows: the plan as the source last read it, the day
/// being pressed, the card, and a skip waiting out its undo window. Every decision is in <see cref="WorkoutsTabModel"/>,
/// <see cref="ActionCardModel"/>, <see cref="SwapChoices"/>, <see cref="MoveHere"/> and <see cref="SkipUndo"/>; this reads,
/// hands them over, and paints.
///
/// <para>Where the weekly pipeline has not been wired, <see cref="Source"/> is <see cref="NoWorkoutsSource"/> and the tab
/// says there is no plan yet, in a sentence, with nothing invented. The plan is read off the UI thread and the page says
/// so in a sentence while it waits.</para>
///
/// <para>A skip happens on the page at once and folds the shell's one dog-ear (the shared notice board) for six seconds.
/// Only when the window closes is it recorded and its calendar event taken off; a turn of the fold inside it undoes it.</para>
/// </summary>
public sealed class WorkoutsArea : ConstitutionAreaBase, IUndoableArea
{
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly SkipUndo undo = new();
    private readonly WorkoutsLog log;

    private WorkoutsRead? read;
    private WorkoutCardView? card;
    private DateOnly? pressed;
    private int loadNumber;

    public WorkoutsArea(Activity host)
        : base(host, "workouts", Resource.Drawable.areaworkouts)
    {
        log = new WorkoutsLog(host);
    }

    /// <summary>Where the plan comes from and where a change goes; set by whoever wires the weekly pipeline.</summary>
    public IWorkoutsSource Source { get; set; } = new NoWorkoutsSource();

    /// <summary>Makes the owner's calendar follow a swap or a skip; null until the pipeline wires one, and then the calendar is left as it is.</summary>
    public WorkoutCalendarFollow? Calendar { get; set; }

    public override string? Subtitle => read is WorkoutsRead.Ready ? WorkoutsTabModel.StatusSentence : null;

    protected override string LoadingSentence => "Reading the week.";

    protected override View BuildPage()
    {
        if (read is not WorkoutsRead.Ready ready)
        {
            return PageFurniture.Sentence(Host, WorkoutsTabModel.NoPlan().Status);
        }

        var model = WorkoutsTabModel.Build(ready.Snapshot, undo.Pending);
        return new WorkoutsTabPage(Host, model, pressed, OnHeld);
    }

    // ---- reading ----

    public override void Resume()
    {
        CommitDue();
        if (read is null)
        {
            Loading = true;
        }

        Load();
        ScheduleCommit();
    }

    public override void Pause()
    {
        handler.RemoveCallbacksAndMessages(null);
        card?.Dismiss();
    }

    private void Load()
    {
        var number = Interlocked.Increment(ref loadNumber);
        var source = Source;
        _ = Task.Run(() =>
        {
            WorkoutsRead result;
            try
            {
                result = source.Read(DateTimeOffset.Now);
            }
            catch (Exception e) when (e is not System.OperationCanceledException)
            {
                // Logged by type only. The tab keeps what it had; with nothing yet it says there is no plan.
                log.Outcome($"plan read failed: {e.GetType().Name}");
                result = read ?? new WorkoutsRead.NoPlanYet();
            }

            Host.RunOnUiThread(() => Adopt(result, number));
        });
    }

    private void Adopt(WorkoutsRead result, int number)
    {
        if (number != Volatile.Read(ref loadNumber))
        {
            return;
        }

        read = result;
        Loading = false;
        RaiseChanged();
    }

    // ---- the card ----

    private void OnHeld(DayClipping day)
    {
        if (read is not WorkoutsRead.Ready ready || !day.CanPress)
        {
            return;
        }

        card?.Dismiss();
        pressed = day.Day;
        RaiseChanged();
        var shown = new WorkoutCardView(Host, OnCardClosed);
        card = shown;
        shown.ShowActions(ActionCardModel.For(day), action => OnAction(action, day, ready.Snapshot, shown));
    }

    private void OnAction(WorkoutActionId action, DayClipping day, WorkoutsSnapshot snapshot, WorkoutCardView shown)
    {
        switch (action)
        {
            case WorkoutActionId.SkipThisDay:
                shown.Dismiss();
                BeginSkip(day, snapshot);
                break;
            case WorkoutActionId.SwapWith:
                shown.ShowChoices(SwapChoices.For(snapshot, day.Day), other => Move(snapshot, day.Day, other, shown));
                break;
            case WorkoutActionId.MoveWorkoutHere:
                shown.ShowChoices(MoveHere.For(snapshot, day.Day), source => Move(snapshot, source, day.Day, shown));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "The card offers no such action.");
        }
    }

    private void OnCardClosed()
    {
        card = null;
        pressed = null;
        RaiseChanged();
    }

    // ---- swap and move ----

    private void Move(WorkoutsSnapshot snapshot, DateOnly from, DateOnly to, WorkoutCardView shown)
    {
        var check = SwapChoices.Check(snapshot, from, to);
        if (check.Result is not { } result)
        {
            // A greyed row has no handler, so this is a day that stopped fitting while the card was open: leave the card as it is.
            return;
        }

        shown.Dismiss();
        read = new WorkoutsRead.Ready(SwapChoices.Apply(snapshot, result));
        RaiseChanged();
        var source = Source;
        var follow = Calendar;
        _ = Task.Run(async () =>
        {
            try
            {
                source.Record(new WorkoutsChange.Swapped(result));
                if (follow is not null)
                {
                    await follow.FollowSwapAsync(result, CancellationToken.None);
                }
            }
            catch (Exception e) when (e is not System.OperationCanceledException)
            {
                log.Outcome($"plan not kept: {e.GetType().Name}");
            }
        });
    }

    // ---- skip, with undo ----

    private void BeginSkip(DayClipping day, WorkoutsSnapshot snapshot)
    {
        var session = snapshot.SessionOn(day.Day);
        if (session is null)
        {
            return;
        }

        var key = WorkoutKeys.Of(snapshot.WeekOf(session.Day)!, snapshot.Person)[session];
        var replaced = ConnectionNoticeBoard.With(board =>
            undo.Begin(board, snapshot.Person, session, snapshot.FactsOn(day.Day).OnCalendar, key, DateTimeOffset.UtcNow));
        if (replaced is not null)
        {
            Commit(replaced);
        }

        RaiseChanged();
        ScheduleCommit();
    }

    /// <summary>The shell turned the fold and the board gave back this act: the skip is undone, as though it had not been asked.</summary>
    public void UndoTurned(string actKey)
    {
        if (undo.Reverse(actKey) is not null)
        {
            handler.RemoveCallbacksAndMessages(null);
        }
    }

    private void ScheduleCommit()
    {
        if (undo.Pending is not { } waiting)
        {
            return;
        }

        handler.RemoveCallbacksAndMessages(null);
        var wait = (long)Math.Max(0, (waiting.Expires - DateTimeOffset.UtcNow).TotalMilliseconds) + 1;
        handler.PostDelayed(() =>
        {
            CommitDue();
            RaiseChanged();
        }, wait);
    }

    private void CommitDue()
    {
        if (undo.Due(DateTimeOffset.UtcNow) is { } due)
        {
            Commit(due);
        }
    }

    private void Commit(SkipCommit commit)
    {
        if (read is not WorkoutsRead.Ready ready)
        {
            return;
        }

        var ledger = commit.Record(ready.Snapshot.Skips);
        read = new WorkoutsRead.Ready(ready.Snapshot with { Skips = ledger });
        var source = Source;
        var follow = Calendar;
        _ = Task.Run(async () =>
        {
            try
            {
                source.Record(new WorkoutsChange.SkipCommitted(commit.Skip, ledger));
                if (follow is not null)
                {
                    await follow.FollowSkipAsync(commit.Skip, CancellationToken.None);
                }
            }
            catch (Exception e) when (e is not System.OperationCanceledException)
            {
                log.Outcome($"record not kept: {e.GetType().Name}");
            }
        });
    }
}
