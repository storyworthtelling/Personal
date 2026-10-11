using Pulp.Domain.Core;

namespace Pulp.Domain.Household;

/// <summary>
/// The one background loop that keeps the household in step with Drive: it sleeps until the schedule's
/// next pass or a wake, runs the pass when it is due, and <b>cannot die</b>. A pass that throws —
/// including a timeout, which is a cancellation carrying no token of ours — is reported, put on the
/// schedule's backoff and followed by the next iteration; only the loop's own stop token ends it. The
/// schedule is guarded here, so every caller changes it through <see cref="Update"/>.
/// </summary>
public sealed class HouseholdSyncLoop
{
    private readonly HouseholdSyncSchedule schedule = new();
    private readonly object gate = new();
    private readonly Func<CancellationToken, Task> pass;
    private readonly Action<Exception> onFault;
    private readonly Func<DateTimeOffset> clock;
    private TaskCompletionSource wakeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HouseholdSyncLoop(Func<CancellationToken, Task> pass, Action<Exception> onFault, Func<DateTimeOffset> clock)
    {
        this.pass = pass ?? throw new ArgumentNullException(nameof(pass));
        this.onFault = onFault ?? throw new ArgumentNullException(nameof(onFault));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Changes the schedule under its lock. Does not wake the loop.</summary>
    public void Update(Action<HouseholdSyncSchedule> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (gate)
        {
            change(schedule);
        }
    }

    /// <summary>The app opened: a pass is due at once (unless the file is blocked).</summary>
    public void Opened() => Update(current => current.Opened(clock()));

    /// <summary>An edit landed: a pass is due after the quiet window.</summary>
    public void Edited()
    {
        Update(current => current.Edited(clock()));
        Wake();
    }

    /// <summary>Someone asked for a pass; due at once, whatever was holding it.</summary>
    public void Ask()
    {
        Update(current => current.Asked(clock()));
        Wake();
    }

    public void Wake() =>
        Interlocked.Exchange(ref wakeSignal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await StepAsync(stop);
            }
            catch (Exception exception) when (CancellationVerdict.IsUserCancel(exception, stop))
            {
                return;
            }
            catch (Exception exception)
            {
                onFault(exception);
                Update(current => current.Failed(clock()));
            }
        }
    }

    private async Task StepAsync(CancellationToken stop)
    {
        var signal = Volatile.Read(ref wakeSignal).Task;
        DateTimeOffset? due;
        bool isDue;
        lock (gate)
        {
            due = schedule.NextDue;
            isDue = schedule.IsDue(clock());
            if (isDue)
            {
                schedule.Started(clock());
            }
        }

        if (isDue)
        {
            await pass(stop);
            return;
        }

        var wait = due is { } at ? at - clock() : Timeout.InfiniteTimeSpan;
        if (wait != Timeout.InfiniteTimeSpan && wait < TimeSpan.Zero)
        {
            wait = TimeSpan.Zero;
        }

        using var delayStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        await Task.WhenAny(signal, Task.Delay(wait, delayStop.Token));
        delayStop.Cancel();
    }
}

/// <summary>Coming back to the app asks for a pass, but at most once a minute, so flicking between apps is not a stream of requests.</summary>
public static class SyncResumeGate
{
    public static readonly TimeSpan Spacing = TimeSpan.FromMinutes(1);

    public static bool ShouldAsk(DateTimeOffset? lastAsked, DateTimeOffset now) =>
        lastAsked is not { } last || now < last || now - last >= Spacing;
}

/// <summary>What the shell does with the dog-ear when the sync status moves: raise the notice, or withdraw whatever the sync raised before.</summary>
public sealed record SyncNoticeChange(Actions.WaitingNotice? Raise, string Key);

/// <summary>Keyed by cause, so a notice leaves when the cause does (mockup 12).</summary>
public static class HouseholdSyncNotices
{
    public static SyncNoticeChange For(HouseholdSyncStatus status, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new SyncNoticeChange(status.Notice(at), Actions.WaitingNotices.HouseholdUnreadable(at).Key);
    }
}
