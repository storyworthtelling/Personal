namespace Codex.Infrastructure.Dash;

/// <summary>
/// The watch-unreachable rule (P14 B2, plan R3: "a quiet line, no buzz"). Post sends the wrist nothing it did not ask
/// for: an answer goes back once, on the node that asked, and is waited for at most <see cref="ReplyWait"/>. An answer
/// that does not land is dropped and counted (<see cref="Missed"/>, a count line in the log and never the payload), not
/// queued and not tried again: the wrist asks again when it is near, an answer kept for later would be stale by then,
/// and a send heard again is answered from the book without being sent twice. The waits are chosen, not measured.
/// </summary>
public sealed class DashReplies
{
    /// <summary>How many times an answer is tried.</summary>
    public const int Attempts = 1;

    /// <summary>The whole of one message from the wrist: waiting its turn, carrying it out, and answering.</summary>
    public static readonly TimeSpan TurnWait = TimeSpan.FromSeconds(60);

    /// <summary>How long a message waits for the one before it (one at a time) before it is dropped as busy.</summary>
    public static readonly TimeSpan GateWait = TimeSpan.FromSeconds(30);

    /// <summary>How long a send waits for a paired bridge to come up: the message may itself have started Post.</summary>
    public static readonly TimeSpan BridgeWait = TimeSpan.FromSeconds(10);

    /// <summary>How often the bridge is looked at while a send waits for it.</summary>
    public static readonly TimeSpan BridgePoll = TimeSpan.FromMilliseconds(500);

    /// <summary>How long one answer to the wrist is waited for.</summary>
    public static readonly TimeSpan ReplyWait = TimeSpan.FromSeconds(15);

    private int missed;

    /// <summary>An answer the wrist did not take; the count since the last one that landed.</summary>
    public int Missed() => Interlocked.Increment(ref missed);

    /// <summary>An answer landed: the count starts again.</summary>
    public void Landed() => Interlocked.Exchange(ref missed, 0);
}
