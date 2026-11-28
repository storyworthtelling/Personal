namespace Codex.Domain.Post;

/// <summary>
/// The newest call-log row as Post reads it the moment the phone goes back to idle: when the call began (the call log's
/// DATE, the moment it was placed or began to ring), how long it was connected (DURATION, the talk alone) and its type
/// (TYPE). The number stays with the head, which needs it for the thread; nothing here judges by it.
/// </summary>
public readonly record struct CallLogRow(DateTimeOffset Started, TimeSpan Connected, int Type);

/// <summary>What became of a call's end: a note offered, or why not. Each is one line in the weekly log.</summary>
public enum CallNoteVerdict
{
    /// <summary>A call someone was on just ended: the note waits behind the dog-ear.</summary>
    Offered,

    /// <summary>The call log held no call at all.</summary>
    NoCall,

    /// <summary>The newest call ended too long ago to be the one that just ended (its row had not landed yet).</summary>
    TooOld,

    /// <summary>Missed, declined, voicemail, blocked, or an outgoing call nobody picked up: nobody was on it.</summary>
    NotAnswered,

    /// <summary>The caller withheld their number, so the note would have no thread to join.</summary>
    Withheld,

    /// <summary>The number is on the phone's blocked list.</summary>
    Blocked,

    /// <summary>The call log would not read (READ_CALL_LOG refused).</summary>
    Unreadable,
}

/// <summary>
/// Whether the newest call-log row is the call that just ended, and one someone was on (the owner's report, P14 B7).
/// The rule this replaced asked how long ago the row's DATE was and allowed fifteen seconds; DATE is when the call
/// began, so every call longer than that, which is every real conversation, was never offered a note. A row is judged
/// by its end instead: when it began, plus the ring before it connected (not in the log; at most
/// <see cref="RingAllowance"/>), plus how long it was connected, which must fall within <see cref="Freshness"/> of now.
/// </summary>
public static class JustEndedCall
{
    // CallLog.Calls.TYPE, the platform's own documented values.
    private const int IncomingType = 1;
    private const int OutgoingType = 2;
    private const int AnsweredExternallyType = 7;

    /// <summary>How soon after a call ends Post reads its row: the broadcast, then a short wait for the log's write.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The ring before a call connected, which the log does not record: an outgoing call can ring most of a minute before
    /// it is answered, or before voicemail takes it. Chosen, not measured.
    /// </summary>
    public static readonly TimeSpan RingAllowance = TimeSpan.FromSeconds(90);

    /// <summary>The log's duration is whole seconds; a row may seem to end a moment after now.</summary>
    public static readonly TimeSpan Early = TimeSpan.FromSeconds(2);

    /// <summary>Whether the row's call ended just now.</summary>
    public static bool Fresh(CallLogRow row, DateTimeOffset now)
    {
        var sinceItsTalkEnded = now - (row.Started + row.Connected);
        return sinceItsTalkEnded >= -Early && sinceItsTalkEnded <= Freshness + RingAllowance;
    }

    /// <summary>
    /// Whether someone was on the call: an incoming or outgoing call that connected, or one answered on another device
    /// (rung here and taken on the watch, whose duration this phone may not know). Anything else is not.
    /// </summary>
    public static bool Answered(CallLogRow row) => row.Type switch
    {
        IncomingType or OutgoingType => row.Connected > TimeSpan.Zero,
        AnsweredExternallyType => true,
        _ => false,
    };

    /// <summary>The verdict on the newest row (null: the log held none). Blocked and withheld are the head's to say.</summary>
    public static CallNoteVerdict Judge(CallLogRow? newest, DateTimeOffset now)
    {
        if (newest is not { } row)
        {
            return CallNoteVerdict.NoCall;
        }
        if (!Fresh(row, now))
        {
            return CallNoteVerdict.TooOld;
        }
        return Answered(row) ? CallNoteVerdict.Offered : CallNoteVerdict.NotAnswered;
    }

    /// <summary>
    /// Whether to read the log again: it is written a moment after the phone goes idle, so a row not there yet (no call,
    /// or the call before it) is worth another look; one that says what the call was is the answer.
    /// </summary>
    /// <summary>
    /// Whether the row's call ended at about <paramref name="moment"/>, the moment the watch heard its own call state go
    /// idle (P14 B7): the same rule as <see cref="Fresh"/>, with <paramref name="slack"/> each way for the watch's clock
    /// against the phone's.
    /// </summary>
    public static bool EndedAbout(CallLogRow row, DateTimeOffset moment, TimeSpan slack)
    {
        var sinceItsTalkEnded = moment - (row.Started + row.Connected);
        return sinceItsTalkEnded >= -slack && sinceItsTalkEnded <= RingAllowance + slack;
    }

    /// <summary>How far the row's talk ended from <paramref name="moment"/>, so the nearest of two calls is the one meant.</summary>
    public static TimeSpan FromMoment(CallLogRow row, DateTimeOffset moment) => (moment - (row.Started + row.Connected)).Duration();

    public static bool ReadAgain(CallNoteVerdict verdict) => verdict is CallNoteVerdict.NoCall or CallNoteVerdict.TooOld;
}
