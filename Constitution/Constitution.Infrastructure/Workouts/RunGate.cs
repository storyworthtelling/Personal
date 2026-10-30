namespace Constitution.Infrastructure.Workouts;

/// <summary>What came of asking the gate for a turn: the run happened and gave its outcome, or it never started because the gate stayed busy for the whole wait.</summary>
public abstract record GateResult<T>
{
    private GateResult()
    {
    }

    /// <summary>The run had its turn and finished; <paramref name="Outcome"/> is what it returned.</summary>
    public sealed record Ran(T Outcome) : GateResult<T>;

    /// <summary>Another run held the gate for the whole wait. This one did not start and wrote nothing; <paramref name="Sentence"/> says so in plain words, naming nothing from a calendar.</summary>
    public sealed record Busy(string Sentence) : GateResult<T>;
}

/// <summary>
/// A run that gave up waiting for its turn (see <see cref="GateResult{T}.Busy"/>), for a caller whose contract is to return the run's outcome and
/// to let anything else propagate: the worker logs the type name and the next opening tries again. Typed, so it is never mistaken for a fault in
/// the run itself.
/// </summary>
public sealed class RunGateBusyException(string sentence) : InvalidOperationException(sentence);

/// <summary>
/// One at a time, in the order they asked, for everything that places events on the household's calendars. The workout run and the meal run each
/// load the shared record of written keys, write the calendar, and save the record again; two at once would save over each other's keys, and a key
/// lost is an event the next run does not recognise as its own (it is written a second time, or a deleted one comes back). So they take turns.
///
/// <list type="bullet">
///   <item><b>Fair.</b> A waiting caller is handed the gate in the order it arrived, by an explicit queue rather than by whichever thread wakes first.</item>
///   <item><b>Bounded.</b> A caller waits at most the time it was given; if the gate has not come free it gets <see cref="GateResult{T}.Busy"/> and
///   its work never starts. A run that never returns holds the gate for good, but it cannot hold anyone else's thread: every other caller is told
///   so within its wait. The gate does not cancel the run it holds; that run's own token does.</item>
///   <item><b>Honest.</b> A run that throws frees the gate and the exception reaches the caller with its stack. A caller whose token is cancelled
///   while waiting leaves the queue and gets the cancellation. Nothing is swallowed; the one thing converted is the caller's own wait running out.</item>
///   <item><b>Not re-entrant.</b> A run that asks for the gate it holds waits for itself until its wait runs out. Nothing inside a run does.</item>
/// </list>
///
/// <para>The gate is per process, which is what <see cref="Shared"/> is for: the weekly worker, the open-time run and the tab share one phone's files.</para>
/// </summary>
public sealed class RunGate
{
    /// <summary>How long a caller waits for its turn unless it says otherwise. Longer than a run takes, shorter than the system gives a background job; the agent's number.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(5);

    private readonly object sync = new();
    private readonly LinkedList<TaskCompletionSource> waiting = new();
    private bool held;

    /// <summary>The process-wide gate the head's runs go through.</summary>
    public static RunGate Shared { get; } = new();

    /// <summary>Waits up to <paramref name="wait"/> for a turn, runs <paramref name="run"/> alone, and frees the gate however it ends.</summary>
    /// <param name="run">The work; given the caller's token.</param>
    /// <param name="wait">The longest to wait for a turn; zero means do not wait.</param>
    /// <param name="cancellation">Cancels the wait (and is handed to the run).</param>
    /// <exception cref="ArgumentOutOfRangeException">The wait is negative.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled before or while waiting.</exception>
    public async Task<GateResult<T>> RunAsync<T>(Func<CancellationToken, Task<T>> run, TimeSpan wait, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero);
        cancellation.ThrowIfCancellationRequested();

        if (!await EnterAsync(wait, cancellation))
        {
            return new GateResult<T>.Busy("Another run was placing events on the calendars, so this one did not start. It will be tried again.");
        }

        try
        {
            return new GateResult<T>.Ran(await run(cancellation));
        }
        finally
        {
            Leave();
        }
    }

    // True when this caller now holds the gate; false when its wait ran out first.
    private async Task<bool> EnterAsync(TimeSpan wait, CancellationToken cancellation)
    {
        LinkedListNode<TaskCompletionSource> place;
        lock (sync)
        {
            if (!held && waiting.Count == 0)
            {
                held = true;
                return true;
            }

            if (wait == TimeSpan.Zero)
            {
                return false;
            }

            place = waiting.AddLast(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(wait);
        try
        {
            await place.Value.Task.WaitAsync(limit.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            // Only our own wait ran out. If the gate was handed to us in the same instant we own it; otherwise we are still queued, and leave.
            lock (sync)
            {
                if (place.List is not null)
                {
                    waiting.Remove(place);
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled. Leave the queue; if the gate had already been handed to us, hand it on, and let the cancellation through.
            bool owned;
            lock (sync)
            {
                owned = place.List is null;
                if (!owned)
                {
                    waiting.Remove(place);
                }
            }

            if (owned)
            {
                Leave();
            }

            throw;
        }
    }

    // The gate goes straight to the longest waiter, still held, so a newcomer cannot slip in between.
    private void Leave()
    {
        lock (sync)
        {
            if (waiting.First is { } next)
            {
                waiting.RemoveFirst();
                next.Value.SetResult();
                return;
            }

            held = false;
        }
    }
}
