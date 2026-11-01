namespace Codex.Domain.Calls;

public enum CallNoticeKind
{
    None,
    Ongoing,
    Incoming,
}

/// <summary>
/// Reads a posted notification as a phone call, because on the locked-down phone a notification
/// is the only thing that says one is happening. Flyleaf's policy disables the status bar, and the
/// device-owner call that does so (<c>SetStatusBarDisabled</c>) also disables notification
/// <em>alerts</em> — so the dialer's ringing heads-up card never appears and there is no shade to
/// pull down to find it. Flyleaf has to bring the call screen forward itself, and needs to know
/// which notifications are calls to do it.
/// </summary>
public static class CallNotice
{
    /// <summary><c>Notification.CATEGORY_CALL</c>.</summary>
    public const string CategoryCall = "call";

    /// <summary>
    /// <c>Notification.EXTRA_CALL_TYPE</c> values (API 31 CallStyle). Absent on a notification
    /// that is not a CallStyle, read as <see cref="CallTypeNone"/>.
    /// </summary>
    public const int CallTypeNone = 0;
    public const int CallTypeIncoming = 1;
    public const int CallTypeOngoing = 2;
    public const int CallTypeScreening = 3;

    /// <summary>
    /// CallStyle's call type decides when there is one. Without it, a call notification carrying a
    /// full-screen intent is ringing — that intent is the platform's "show this over everything",
    /// which is what a dialer asks for while a call rings — and an ongoing one is in progress.
    /// Only the call category counts: an alarm also carries a full-screen intent.
    /// </summary>
    public static CallNoticeKind Classify(string? category, int callType, bool hasFullScreenIntent, bool isOngoing)
    {
        if (category != CategoryCall)
        {
            return CallNoticeKind.None;
        }

        return callType switch
        {
            CallTypeIncoming => CallNoticeKind.Incoming,
            CallTypeOngoing or CallTypeScreening => CallNoticeKind.Ongoing,
            _ when hasFullScreenIntent => CallNoticeKind.Incoming,
            _ when isOngoing => CallNoticeKind.Ongoing,
            _ => CallNoticeKind.None,
        };
    }

    /// <summary>A ringing call outranks one in progress: call waiting is the call that needs a tap.</summary>
    public static CallNoticeKind Strongest(IEnumerable<CallNoticeKind> kinds) =>
        kinds.DefaultIfEmpty(CallNoticeKind.None).Max();

    /// <summary>The home screen's way back into the call, or null when there is no call.</summary>
    /// <summary>
    /// The call-surfacing decision for the log loop (LOG-LOOP-PLAN.md §4.5): what kind of call, and whether
    /// Flyleaf brought the call screen forward — the record a call that rang unseen would leave.
    /// </summary>
    public static string Line(CallNoticeKind kind, bool surfaced, bool opened = true) =>
        !surfaced ? $"CALL {kind}: already surfaced for this ring"
        : opened ? $"CALL {kind}: surfaced the call screen"
        : $"CALL {kind}: the call screen did not open";

    public static string? ActionLabel(CallNoticeKind kind) => kind switch
    {
        CallNoticeKind.Incoming => "Answer the call",
        CallNoticeKind.Ongoing => "Return to the call",
        _ => null,
    };
}
