namespace FocusPhone.Domain.Music;

/// <summary>What a headset-button gesture resolves to.</summary>
public enum MediaButtonAction
{
    PlayPause,
    Next,
    Previous,
}

/// <summary>
/// Interprets headset media-button presses eyes-free: one press play/pauses, a quick double skips forward, a
/// triple skips back — the familiar wired-remote gestures. Presses within the window accumulate; once the
/// window since the last press elapses, the count resolves to an action. Pure and stateful; the Android side
/// feeds it button events and a clock.
/// </summary>
public sealed class MediaButtonGesture
{
    private readonly TimeSpan window;
    private int count;
    private DateTimeOffset lastPress;

    public MediaButtonGesture(TimeSpan window) => this.window = window;

    /// <summary>Records a button press; a press after the window resets the run.</summary>
    public void Press(DateTimeOffset at)
    {
        if (count > 0 && at - lastPress > window)
        {
            count = 0;
        }

        count++;
        lastPress = at;
    }

    /// <summary>Returns the resolved action once the window since the last press has elapsed, else null.</summary>
    public MediaButtonAction? Flush(DateTimeOffset now)
    {
        if (count == 0 || now - lastPress < window)
        {
            return null;
        }

        var action = count switch
        {
            1 => MediaButtonAction.PlayPause,
            2 => MediaButtonAction.Next,
            _ => MediaButtonAction.Previous,
        };
        count = 0;
        return action;
    }
}
