namespace Constitution.Domain.Workouts.Screens;

/// <summary>
/// What a change did, for the tab to show. <see cref="Kept"/> is whether it was written down; <see cref="Sentence"/> is the one plain line
/// the tab says about it (null when there is nothing to say); <see cref="Failure"/> is what stopped it, kept for the trail, never shown.
/// <see cref="Busy"/> is true only when the change never started because another plan was being written for the whole wait: nothing was
/// written, the sentence is <see cref="FollowWords.GateBusy"/>, and it is not a fault.
/// </summary>
public sealed record ChangeResult(bool Kept, string? Sentence, Exception? Failure = null, bool Busy = false);

/// <summary>
/// The owner's swaps and skips on the Workouts tab, taken through their steps in the order that keeps them honest (S2): the change is
/// written down first, then the calendar is asked to follow, and what the calendar answers is read into a sentence (<see cref="FollowWords"/>)
/// and handed back, never dropped. It was a fire-and-forget task in the head, which threw the answer away, logged a failed write to a trail and
/// left the tab showing a change that the next reload quietly took back.
///
/// <list type="bullet">
///   <item><b>A change that could not be written down is not kept</b> (<see cref="ChangeResult.Kept"/> false): the calendar is not touched and
///   the tab takes the change back (a swap) or keeps the skip owed (a skip).</item>
///   <item><b>A calendar that did not change, and moved nothing, is asked again</b> (up to <see cref="MostTries"/> in all) with the sentence
///   standing meanwhile. One that moved only some of a swap's events is not: asking again would move the wrong ones, so the owner is told.</item>
///   <item>The failures the sources and the calendar can name (<see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>,
///   <see cref="InvalidOperationException"/> from the source; <see cref="HttpRequestException"/>, a timeout, the same I/O ones from the calendar)
///   come back in the result. Anything else is a bug and propagates, as does the caller's own cancellation.</item>
/// </list>
///
/// <para>Retries live in memory: a skip is written down before the calendar is asked, and the weekly run plans the week the owner left, so
/// what is lost when the app is closed is only the second attempt at the calendar, not the change.</para>
/// </summary>
public sealed class WorkoutChanges(IWorkoutsSource source, WorkoutCalendarFollow? calendar)
{
    /// <summary>How many times the calendar is asked to follow one change, the first included.</summary>
    public const int MostTries = 3;

    private readonly IWorkoutsSource source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly List<Owed> owed = [];
    private readonly object ownedGate = new();
    private readonly SemaphoreSlim oneAtATime = new(1, 1);

    /// <summary>Whether the calendar still owes a change another try.</summary>
    public bool HasOwed
    {
        get
        {
            lock (ownedGate)
            {
                return owed.Count > 0;
            }
        }
    }

    /// <summary>Writes the swap down, then asks the calendar to follow.</summary>
    public async Task<ChangeResult> SwapAsync(SwapResult swap, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(swap);
        await oneAtATime.WaitAsync(cancellation);
        try
        {
            if (Keep(new WorkoutsChange.Swapped(swap)) is { } notKept)
            {
                return new ChangeResult(false, FollowWords.SwapNotKept, notKept);
            }

            if (calendar is null)
            {
                return new ChangeResult(true, null);
            }

            var (reading, failure) = await FollowAsync(Owed.OfSwap(swap), cancellation);
            if (reading.WillRetry)
            {
                Owe(Owed.OfSwap(swap));
            }

            return new ChangeResult(true, reading.Sentence, failure);
        }
        finally
        {
            oneAtATime.Release();
        }
    }

    /// <summary>Writes the skip down (the skip alone; the source settles it against the ledger as it stands), then asks the calendar to take the event off.</summary>
    public async Task<ChangeResult> SkipAsync(PendingSkip skip, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(skip);
        await oneAtATime.WaitAsync(cancellation);
        try
        {
            if (Keep(new WorkoutsChange.SkipCommitted(skip)) is { } notKept)
            {
                return new ChangeResult(false, FollowWords.SkipNotKept, notKept);
            }

            if (calendar is null)
            {
                return new ChangeResult(true, null);
            }

            var (reading, failure) = await FollowAsync(Owed.OfSkip(skip), cancellation);
            if (reading.WillRetry)
            {
                Owe(Owed.OfSkip(skip));
            }

            return new ChangeResult(true, reading.Sentence, failure);
        }
        finally
        {
            oneAtATime.Release();
        }
    }

    /// <summary>Asks the calendar again for every change it still owes, once each. The sentence is what the tab should now say (null once nothing is owed).</summary>
    public async Task<ChangeResult> RetryAsync(CancellationToken cancellation)
    {
        await oneAtATime.WaitAsync(cancellation);
        try
        {
            string? sentence = null;
            Exception? lastFailure = null;
            foreach (var change in Owing())
            {
                var (reading, failure) = await FollowAsync(change, cancellation);
                lastFailure = failure ?? lastFailure;
                change.Tries++;
                if (reading.WillRetry && change.Tries < MostTries)
                {
                    continue;
                }

                Release(change);
                sentence = reading.WillRetry ? FollowWords.CalendarGaveUp : reading.Sentence ?? sentence;
            }

            return new ChangeResult(true, HasOwed ? FollowWords.CalendarUnchanged : sentence, lastFailure);
        }
        finally
        {
            oneAtATime.Release();
        }
    }

    private void Owe(Owed change)
    {
        lock (ownedGate)
        {
            owed.Add(change);
        }
    }

    private void Release(Owed change)
    {
        lock (ownedGate)
        {
            owed.Remove(change);
        }
    }

    private List<Owed> Owing()
    {
        lock (ownedGate)
        {
            return owed.ToList();
        }
    }

    // The failure that kept the change from being written down, or null when it was.
    private Exception? Keep(WorkoutsChange change)
    {
        try
        {
            source.Record(change);
            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return failure;
        }
    }

    private async Task<(FollowReading Reading, Exception? Failure)> FollowAsync(Owed change, CancellationToken cancellation)
    {
        try
        {
            if (change.Swap is { } swap)
            {
                return (FollowWords.Judge(await calendar!.FollowSwapAsync(swap, cancellation)), null);
            }

            return (FollowWords.Judge([await calendar!.FollowSkipAsync(change.Skip!, cancellation)]), null);
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException or UnauthorizedAccessException
            || (failure is TaskCanceledException timedOut && timedOut.InnerException is TimeoutException && !cancellation.IsCancellationRequested))
        {
            // Which events moved before it fell over is not known. A skip moves one event, so asking again is safe; a swap moves two,
            // so the owner is told it may be half done rather than the pair being tried again.
            var reading = change.Swap is not null
                ? new FollowReading(FollowVerdict.PartlyChanged, FollowWords.CalendarPartlyChanged, false)
                : new FollowReading(FollowVerdict.NotChanged, FollowWords.CalendarUnchanged, true);
            return (reading, failure);
        }
    }

    private sealed class Owed
    {
        public SwapResult? Swap { get; private init; }

        public PendingSkip? Skip { get; private init; }

        public int Tries { get; set; } = 1;

        public static Owed OfSwap(SwapResult swap) => new() { Swap = swap };

        public static Owed OfSkip(PendingSkip skip) => new() { Skip = skip };
    }
}
