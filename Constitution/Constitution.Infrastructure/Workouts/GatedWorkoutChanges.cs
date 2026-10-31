using Constitution.Domain.Workouts.Screens;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The Workouts tab's swap, skip and calendar retry, taking their turn on the shared <see cref="RunGate"/>. They edit calendar events and load,
/// change and save the written-keys record, exactly as the meal run, the workout run and the deadline step do, so without a turn they could
/// save over one another's keys. The whole change, its write-down included, happens inside the turn; a change that cannot get a turn within
/// <c>wait</c> never starts, writes nothing, and comes back as a <see cref="ChangeResult"/> with <see cref="ChangeResult.Busy"/> set and the
/// plain sentence <see cref="FollowWords.GateBusy"/>, which the tab shows under its header (no toast, no dialog).
///
/// <para>The wait is a task, not a block: call these from off the UI thread as the tab does, and the thread is never held. A swap that comes
/// back busy is not kept, so the tab takes it back; a skip that comes back busy is not kept either, so it stays owed and is tried again; a
/// busy retry leaves the change owed. Cancellation by the caller propagates, and nothing is swallowed.</para>
/// </summary>
public sealed class GatedWorkoutChanges(WorkoutChanges changes, RunGate gate, TimeSpan wait)
{
    private readonly WorkoutChanges changes = changes ?? throw new ArgumentNullException(nameof(changes));
    private readonly RunGate gate = gate ?? throw new ArgumentNullException(nameof(gate));

    /// <summary>Whether the calendar still owes a change another try.</summary>
    public bool HasOwed => changes.HasOwed;

    /// <summary>Writes the swap down and has the calendar follow, once the gate is free.</summary>
    public async Task<ChangeResult> SwapAsync(SwapResult swap, CancellationToken cancellation) =>
        Answer(await gate.RunAsync(turn => changes.SwapAsync(swap, turn), wait, cancellation));

    /// <summary>Writes the skip down and has the calendar take the event off, once the gate is free.</summary>
    public async Task<ChangeResult> SkipAsync(PendingSkip skip, CancellationToken cancellation) =>
        Answer(await gate.RunAsync(turn => changes.SkipAsync(skip, turn), wait, cancellation));

    /// <summary>Asks the calendar again for every change it still owes, once the gate is free.</summary>
    public async Task<ChangeResult> RetryAsync(CancellationToken cancellation) =>
        Answer(await gate.RunAsync(turn => changes.RetryAsync(turn), wait, cancellation));

    private static ChangeResult Answer(GateResult<ChangeResult> result) => result switch
    {
        GateResult<ChangeResult>.Ran ran => ran.Outcome,
        GateResult<ChangeResult>.Busy => new ChangeResult(false, FollowWords.GateBusy, null, true),
        _ => throw new InvalidOperationException("The gate gave an answer that is neither a run nor busy."),
    };
}
