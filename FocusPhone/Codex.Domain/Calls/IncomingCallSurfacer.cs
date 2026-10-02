namespace Codex.Domain.Calls;

/// <summary>
/// Decides when Flyleaf pulls the call screen forward: once each time a call starts ringing,
/// not on every re-post of the ringing notification. A dialer rewrites its notification while a
/// call rings (the caller's name resolving, a spam verdict landing), and if the owner pressed Home
/// to let it ring, re-surfacing on each rewrite would drag them back into the call screen over and
/// over. Dialers also keep one notification key for the in-call notice and rewrite it from ringing
/// to ongoing and, for call waiting, back to ringing — so the state is the last kind seen per key,
/// and a ring surfaces on the transition into ringing.
/// </summary>
public sealed class IncomingCallSurfacer
{
    private readonly Dictionary<string, CallNoticeKind> lastSeen = [];

    public bool ShouldSurface(string key, CallNoticeKind kind)
    {
        var wasRinging = lastSeen.TryGetValue(key, out var previous) && previous == CallNoticeKind.Incoming;
        lastSeen[key] = kind;
        return kind == CallNoticeKind.Incoming && !wasRinging;
    }

    public void Removed(string key) => lastSeen.Remove(key);
}
