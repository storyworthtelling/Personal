namespace Watch.Domain.Dash;

/// <summary>The platform's word for the watch's call state (<c>TelephonyManager</c>'s idle, ringing, off hook).</summary>
public enum CallState
{
    Idle,
    Ringing,
    OffHook,
}

/// <summary>
/// A call the watch heard end (P14 B7, W-5.1.5): when it went idle, and when it went off hook if this process heard that
/// (a process can die during a long call). Nothing about who it was: the watch's own Phone app had the call.
/// </summary>
public sealed record CallEnd(DateTimeOffset Ended, DateTimeOffset? Began)
{
    /// <summary>From off hook to idle, as the wrist heard it; null when it did not hear the call begin.</summary>
    public TimeSpan? Length => Began is { } began && Ended > began ? Ended - began : null;
}

/// <summary>
/// The call note after a system call (W-5.1.5, A13; mock: After the call). The watch's own Phone app rings and carries
/// the call (A59), so Dash learns only what the platform tells any app holding READ_PHONE_STATE: the call state going off
/// hook and then idle. No number, no name: those are Post's, asked for by the moment the call ended
/// (<see cref="DashCallAsk"/>), and the watch never reads a number of its own.
///
/// <para>A call ends when the state goes idle after off hook, or when a process that heard nothing before hears idle (it
/// was born after the call began, the one before it having died during the call). A ring that stops without going off
/// hook was a call nobody was on. The end waits, in memory and never written down, for the next time Dash opens: Android
/// does not let an app open a screen from the background, so Dash asks then, once, within <see cref="OfferFor"/>. One
/// offer at a time, as the phone keeps one: a newer call takes its place.</para>
/// </summary>
public sealed class AfterCall
{
    /// <summary>How long a call's end is worth a note on the wrist. Chosen, not measured.</summary>
    public static readonly TimeSpan OfferFor = TimeSpan.FromMinutes(30);

    private readonly object gate = new();
    private CallState? last;
    private DateTimeOffset? offHookAt;
    private CallEnd? offer;

    /// <summary>The one the receiver and People waiting share, for the process.</summary>
    public static AfterCall Shared { get; } = new();

    /// <summary>A call state the platform said at <paramref name="at"/>; the end it makes, if it ends a call.</summary>
    public CallEnd? Heard(CallState state, DateTimeOffset at)
    {
        lock (gate)
        {
            var before = last;
            last = state;
            switch (state)
            {
                case CallState.OffHook:
                    // Call waiting or a held call can say off hook again; the call began at the first.
                    offHookAt ??= at;
                    return null;
                case CallState.Ringing:
                    if (before != CallState.OffHook)
                    {
                        offHookAt = null;
                    }
                    return null;
                default:
                    var began = offHookAt;
                    offHookAt = null;
                    if (before is CallState.Idle or CallState.Ringing)
                    {
                        // Idle again, or a ring nobody answered: no call ended.
                        return null;
                    }
                    offer = new CallEnd(at, began);
                    return offer;
            }
        }
    }

    /// <summary>The call to ask about now: the last one that ended, if it is not settled and not too long ago.</summary>
    public CallEnd? Due(DateTimeOffset now)
    {
        lock (gate)
        {
            return offer is { } end && now - end.Ended <= OfferFor ? end : null;
        }
    }

    /// <summary>The offer was made, or there was nothing to note: it is not made again. A newer one stands.</summary>
    public void Settle(CallEnd end)
    {
        lock (gate)
        {
            if (offer == end)
            {
                offer = null;
            }
        }
    }

    /// <summary>
    /// How long the call was, for "6 min, just now": the phone's log when it has the talk (the wrist's own timing runs from
    /// off hook, which for a call placed includes the ring), else what the wrist heard; null when neither knows.
    /// </summary>
    public static TimeSpan? LengthOf(DashCall call, CallEnd end) =>
        call.Length is { } logged && logged > TimeSpan.Zero ? logged : end.Length;
}
