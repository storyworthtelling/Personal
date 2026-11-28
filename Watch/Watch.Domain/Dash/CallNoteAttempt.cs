namespace Watch.Domain.Dash;

/// <summary>
/// The owner's note on a call, dictated on the wrist (W-5.1.5, A13; P14 B7), always on a call Post named by its message id
/// (<see cref="DashCall.Call"/>), never on a number: B1's contract can carry one for a call only the watch had, and the
/// wrist never uses it, since it reads no number of its own. Post keeps the note where it keeps call notes, named by the
/// moment it was written (<see cref="At"/>), so a note told again after a lost answer is kept once
/// (<see cref="Keep"/>: the same words go again as the same note; new words are a new one).
/// </summary>
public sealed record CallNoteAttempt(string Call, string Text, DateTimeOffset At)
{
    public DashCallNote Message => new(Call, null, Text) { At = At };

    /// <param name="at">The moment the note is written, which names it.</param>
    public static CallNoteAttempt For(DashCall call, string words, DateTimeOffset at)
    {
        if (call.Call is not { } id || !DashIds.IsMessage(id))
        {
            throw new ArgumentException("A note goes on a call the phone named; this answer named none.", nameof(call));
        }
        if (Check(words) != SendCheck.Fits)
        {
            throw new ArgumentException($"A note must have words, and at most {DashLimits.SendText} characters; these are {Check(words)}.", nameof(words));
        }
        return new CallNoteAttempt(id, words.Trim(), at);
    }

    /// <summary>The note to tell the phone when Keep is tapped: the one before, if its words are these, else a new one.</summary>
    public static CallNoteAttempt Keep(CallNoteAttempt? previous, DashCall call, string words, DateTimeOffset at) =>
        previous is not null && previous.Call == call.Call && previous.Text == words?.Trim() ? previous : For(call, words!, at);

    /// <summary>Whether the words can go as one note: the same bound as a text from the wrist.</summary>
    public static SendCheck Check(string? words) => SendAttempt.Check(words);
}
