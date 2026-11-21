using Watch.Domain.Design;

namespace Watch.Domain.Touch;

/// <summary>Something the watch may tell the wrist about.</summary>
public enum WatchEvent
{
    Call,
    Text,
    Mail,
    RestEnded,
    EaseOff,
}

/// <summary>Where a Beat workout stands: none running, in a set, or in a rest.</summary>
public enum SessionState
{
    None,
    InSet,
    Resting,
}

public enum HapticAction
{
    /// <summary>No buzz, now or later.</summary>
    Silent,

    /// <summary>No buzz yet: kept until the set ends (A26).</summary>
    Hold,

    /// <summary>Buzz the decision's pattern now.</summary>
    Buzz,
}

/// <summary>What to do with the wrist; <see cref="Pattern"/> is set only for <see cref="HapticAction.Buzz"/>.</summary>
public readonly record struct HapticDecision(HapticAction Action, HapticPattern? Pattern)
{
    public static HapticDecision Silent { get; } = new(HapticAction.Silent, null);

    public static HapticDecision Hold { get; } = new(HapticAction.Hold, null);

    public static HapticDecision Buzz(HapticPattern pattern) => new(HapticAction.Buzz, pattern);
}

/// <summary>
/// Which of the four shapes the wrist feels (WD §7, A35, O4; P14 W-1.3.1), one meaning each: a call
/// rings in any state; a text buzzes, except during a set, when it is held and the held texts buzz once
/// as the rest (or the session's end) begins (A26); a rest's end buzzes only in a rest; ease off always
/// buzzes; mail never does (A11).
/// </summary>
public static class HapticPlan
{
    public static HapticDecision For(WatchEvent happened, SessionState state) => happened switch
    {
        WatchEvent.Call => HapticDecision.Buzz(WatchTokens.Haptics.Call),
        WatchEvent.Text when state == SessionState.InSet => HapticDecision.Hold,
        WatchEvent.Text => HapticDecision.Buzz(WatchTokens.Haptics.Text),
        WatchEvent.Mail => HapticDecision.Silent,
        WatchEvent.RestEnded when state == SessionState.Resting => HapticDecision.Buzz(WatchTokens.Haptics.RestEnd),
        WatchEvent.RestEnded => HapticDecision.Silent,
        WatchEvent.EaseOff => HapticDecision.Buzz(WatchTokens.Haptics.EaseOff),
        _ => throw new ArgumentOutOfRangeException(nameof(happened), happened, "Not a watch event the plan knows."),
    };

    /// <summary>
    /// The session moved: leaving a set with texts held releases them as one Text buzz, however many
    /// there were. Anything else is silent.
    /// </summary>
    public static HapticDecision Moved(SessionState from, SessionState to, int heldTexts) =>
        from == SessionState.InSet && to != SessionState.InSet && heldTexts > 0
            ? HapticDecision.Buzz(WatchTokens.Haptics.Text)
            : HapticDecision.Silent;

    /// <summary>The one thing a pattern means.</summary>
    public static WatchEvent MeaningOf(HapticPattern pattern)
    {
        if (ReferenceEquals(pattern, WatchTokens.Haptics.Call))
        {
            return WatchEvent.Call;
        }
        if (ReferenceEquals(pattern, WatchTokens.Haptics.Text))
        {
            return WatchEvent.Text;
        }
        if (ReferenceEquals(pattern, WatchTokens.Haptics.RestEnd))
        {
            return WatchEvent.RestEnded;
        }
        if (ReferenceEquals(pattern, WatchTokens.Haptics.EaseOff))
        {
            return WatchEvent.EaseOff;
        }
        throw new ArgumentException($"{pattern.Name} is not one of the four watch patterns.", nameof(pattern));
    }
}
