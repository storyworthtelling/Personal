using Android.App;
using Android.OS;
using Android.Views;
using Constitution.Domain.Notices;
using Constitution.Domain.Workouts.Screens;
using Constitution.Infrastructure.Workouts;
using Constitution.Settings;
using Constitution.WorkoutsTab;

namespace Constitution;

/// <summary>
/// The Workouts tab (C-5.4, book page 12). The area owns what the tab shows: the plan as the source last read it, the day
/// being pressed, the card, and a skip waiting out its undo window. Every decision is in <see cref="WorkoutsTabModel"/>,
/// <see cref="ActionCardModel"/>, <see cref="SwapChoices"/>, <see cref="MoveHere"/> and <see cref="SkipUndo"/>; this reads,
/// hands them over, and paints.
///
/// <para><see cref="WorkoutsTabServices.Wire"/> gives the area its source (the weekly run's saved week) and its calendar follow. Where
/// there is no saved week, <see cref="Source"/> answers <see cref="WorkoutsRead.NoPlanYet"/> and the tab
/// says there is no plan yet, in a sentence, with nothing invented. The plan is read off the UI thread and the page says
/// so in a sentence while it waits.</para>
///
/// <para>A skip happens on the page at once and folds the shell's one dog-ear (the shared notice board) for six seconds.
/// Only when the window closes is it recorded and its calendar event taken off; a turn of the fold inside it undoes it.</para>
///
/// <para><b>A change is never fire-and-forget</b> (S2). Swaps and skips go through <see cref="WorkoutChanges"/>, which writes the change
/// down, asks the calendar to follow and reads what it answered into a sentence; the sentence is the line under the header
/// (<see cref="WorkoutsTabModel.StatusWith"/>), so a calendar that did not change says so in words. A swap that could not be written down is
/// taken back on the page. A skip stays on show and owed until it has been recorded, and is tried again; closing its window never loses it,
/// and neither does a tab that had not finished loading. A calendar that did not change and moved nothing is asked again when the tab is
/// next opened.</para>
/// </summary>
public sealed class WorkoutsArea : ConstitutionAreaBase, IUndoableArea
{
    // How long a skip that could not be written down waits before it is tried again while the tab stays open.
    private const long RetryMilliseconds = 30_000;

    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly SkipUndo undo = new();
    private readonly WorkoutsLog log;
    private readonly HashSet<DateOnly> recording = [];

    private IWorkoutsSource source = new NoWorkoutsSource();
    private WorkoutCalendarFollow? calendar;
    private GatedWorkoutChanges? changes;
    private WorkoutsRead? read;
    private WorkoutCardView? card;
    private DateOnly? pressed;
    private string? notice;
    private int loadNumber;

    public WorkoutsArea(Activity host)
        : base(host, "workouts", Resource.Drawable.areaworkouts)
    {
        log = new WorkoutsLog(host);
        WorkoutsTabServices.Wire(host, this);
    }

    /// <summary>Where the plan comes from and where a change goes; set by whoever wires the weekly pipeline.</summary>
    public IWorkoutsSource Source
    {
        get => source;
        set
        {
            source = value;
            changes = null;
        }
    }

    /// <summary>Makes the owner's calendar follow a swap or a skip; null until the pipeline wires one, and then the calendar is left as it is.</summary>
    public WorkoutCalendarFollow? Calendar
    {
        get => calendar;
        set
        {
            calendar = value;
            changes = null;
        }
    }

    // Every change takes its turn on the shared gate with the meal run, the workout run and the deadline step, and waits for it off the UI thread.
    private GatedWorkoutChanges Changes => changes ??= new GatedWorkoutChanges(new WorkoutChanges(source, calendar), RunGate.Shared, RunGate.DefaultWait);

    public override string? Subtitle => read is WorkoutsRead.Ready ? WorkoutsTabModel.StatusWith(notice) : null;

    protected override string LoadingSentence => "Reading the week.";

    protected override View BuildPage()
    {
        if (read is not WorkoutsRead.Ready ready)
        {
            // A saved week that is there and cannot be read says so in its own sentence; no week at all is the plain "no plan yet".
            return PageFurniture.Sentence(Host, read is WorkoutsRead.Unavailable unavailable ? unavailable.Reason : WorkoutsTabModel.NoPlan().Status);
        }

        var model = WorkoutsTabModel.Build(ready.Snapshot, undo.Shown);
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
        RetryTheCalendar();
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
        var previous = read;
        var optimistic = new WorkoutsRead.Ready(SwapChoices.Apply(snapshot, result));
        read = optimistic;
        RaiseChanged();
        var changes = Changes;
        _ = Task.Run(async () =>
        {
            var outcome = await Settle(() => changes.SwapAsync(result, CancellationToken.None), FollowWords.SwapNotKept, "plan not kept");
            Host.RunOnUiThread(() => SwapDone(previous, optimistic, outcome));
        });
    }

    private void SwapDone(WorkoutsRead? previous, WorkoutsRead optimistic, ChangeResult outcome)
    {
        if (!outcome.Kept)
        {
            // It could not be written down, so the plan is as it was: the page takes it back rather than waiting for a reload to.
            if (ReferenceEquals(read, optimistic) && previous is not null)
            {
                read = previous;
            }
            else
            {
                Load();
            }
        }

        SaySo(outcome.Sentence);
    }

    // ---- skip, with undo ----

    private void BeginSkip(DayClipping day, WorkoutsSnapshot snapshot)
    {
        var session = snapshot.SessionOn(day.Day);
        if (session is null)
        {
            return;
        }

        var key = WorkoutKeys.Of(snapshot.WeekOf(session.Day)!, snapshot.Person, snapshot.Assignments)[session];
        var replaced = ConnectionNoticeBoard.With(board =>
            undo.Begin(board, snapshot.Person, session, snapshot.FactsOn(day.Day).OnCalendar, key, DateTimeOffset.UtcNow));
        if (replaced is not null)
        {
            StartRecording(replaced);
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
        long wait;
        if (undo.Pending is { } waiting)
        {
            wait = (long)Math.Max(0, (waiting.Expires - DateTimeOffset.UtcNow).TotalMilliseconds) + 1;
        }
        else if (undo.Unrecorded.Count > 0)
        {
            wait = RetryMilliseconds;
        }
        else
        {
            return;
        }

        handler.RemoveCallbacksAndMessages(null);
        handler.PostDelayed(() =>
        {
            CommitDue();
            RaiseChanged();
            ScheduleCommit();
        }, wait);
    }

    // The window of a skip has closed, or one that could not be written down is owed another go: record each, once at a time.
    private void CommitDue()
    {
        if (undo.Due(DateTimeOffset.UtcNow) is { } due)
        {
            StartRecording(due);
        }

        foreach (var owed in undo.Unrecorded)
        {
            StartRecording(owed);
        }
    }

    // Records one skip whether or not the tab has finished loading: the skip is the owner's word and does not wait on a page.
    private void StartRecording(SkipCommit commit)
    {
        if (!recording.Add(commit.Skip.Day))
        {
            return;
        }

        var changes = Changes;
        _ = Task.Run(async () =>
        {
            var result = await Settle(() => changes.SkipAsync(commit.Skip, CancellationToken.None), FollowWords.SkipNotKept, "record not kept");
            Host.RunOnUiThread(() => RecordingDone(commit, result));
        });
    }

    private void RecordingDone(SkipCommit commit, ChangeResult result)
    {
        recording.Remove(commit.Skip.Day);
        if (result.Kept)
        {
            // Let go of only now that it is written down. The page shows the day skipped from the ledger as it was loaded, plus this.
            undo.Recorded(commit);
            if (read is WorkoutsRead.Ready ready)
            {
                read = new WorkoutsRead.Ready(ready.Snapshot with { Skips = commit.Record(ready.Snapshot.Skips) });
            }
        }

        SaySo(result.Sentence);
        if (!result.Kept)
        {
            ScheduleCommit();
        }
    }

    // ---- the calendar, asked again ----

    private void RetryTheCalendar()
    {
        if (!Changes.HasOwed)
        {
            return;
        }

        var changes = Changes;
        _ = Task.Run(async () =>
        {
            var result = await Settle(() => changes.RetryAsync(CancellationToken.None), FollowWords.CalendarUnchanged, "calendar not kept");
            Host.RunOnUiThread(() => SaySo(result.Sentence));
        });
    }

    // ---- what the tab says ----

    private void SaySo(string? sentence)
    {
        notice = sentence ?? (Changes.HasOwed ? FollowWords.CalendarUnchanged : null);
        RaiseChanged();
    }

    // Runs a change off the UI thread and keeps what stopped it, by type only, in the trail. A failure the models do not name is still
    // reported as a change that was not kept, never swallowed into silence.
    private async Task<ChangeResult> Settle(Func<Task<ChangeResult>> change, string sentenceIfNotKept, string phrase)
    {
        try
        {
            var result = await change();
            if (result.Failure is { } failure)
            {
                log.Outcome($"{phrase}: {failure.GetType().Name}");
            }

            return result;
        }
        catch (Exception e) when (e is not System.OperationCanceledException)
        {
            log.Outcome($"{phrase}: {e.GetType().Name}");
            return new ChangeResult(false, sentenceIfNotKept, e);
        }
    }
}
