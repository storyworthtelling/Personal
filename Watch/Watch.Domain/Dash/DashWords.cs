namespace Watch.Domain.Dash;

/// <summary>What stands between the wrist and an answer from the phone.</summary>
public enum DashTrouble
{
    /// <summary>No phone carrying Post's relay is within reach, near or through the cloud.</summary>
    NoPhone,

    /// <summary>The phone was asked and did not answer within the wrist's wait.</summary>
    NoAnswer,

    /// <summary>The phone answered with something this watch cannot read (a newer contract, or a damaged one).</summary>
    Unreadable,

    /// <summary>Neither the system's dictation nor its keyboard could be opened.</summary>
    NoInput,

    /// <summary>The phone's Rolodex changed while its pages were asked for, again and again (B6).</summary>
    Changing,
}

/// <summary>
/// Dash's fixed words (WD §6, A37; P14 B4): one short sentence at most, never an exclamation. Loading is a sentence
/// that says what is happening; success is silent (the screen simply changes); an error is a quiet line that says what
/// went wrong, in the secondary grey, never a buzz. No app is named on the watch (A48), so the phone is "your phone".
/// The labels are the mockups' (People waiting, A conversation, Replying by voice, A group text with a photo, Filed with
/// Undo); File and Delete, the choice before an act that no mockup draws, are the phone's own words for the two acts.
/// No mockup draws a new text either (B6): its words follow the nearest drawn screens, and the phone's Rolodex. After the
/// call and Mail are drawn (B7); the note read back is not, and takes the phone's own call note's words.
/// </summary>
public static class DashWords
{
    /// <summary>People waiting, asked for.</summary>
    public const string Finding = "Finding who’s waiting.";

    /// <summary>A conversation, asked for.</summary>
    public const string Opening = "Opening the conversation.";

    /// <summary>A reply on its way to the phone.</summary>
    public const string Sending = "Sending.";

    /// <summary>A typed reply past what one text from the wrist may carry (<see cref="DashLimits.SendText"/>).</summary>
    public const string TooLong = "That’s too long for one text.";

    public const string Reply = "Reply";

    /// <summary>A group's reply: one text to everyone (A10).</summary>
    public const string ReplyToAll = "Reply to all";

    public const string Send = "Send";

    /// <summary>The system's own keyboard, one tap from the dictated words (mock: Replying by voice).</summary>
    public const string Keyboard = "Keyboard";

    /// <summary>Dictate the reply again.</summary>
    public const string Again = "Again";

    /// <summary>Who a reply goes to, over the words read back.</summary>
    public static string To(string name) => $"To {name}";

    // ---- B5: filing and deleting (W-5.1.4), and photos (W-5.1.3) ----

    public const string File = "File";

    public const string Delete = "Delete";

    public const string TapToUndo = "Tap to undo";

    /// <summary>A row's long press, as TalkBack names it.</summary>
    public const string FileOrDelete = "File or delete";

    /// <summary>The act stands: the undo never reached the phone.</summary>
    public const string UndoLost = "The undo didn’t reach your phone.";

    /// <summary>A photo, asked for.</summary>
    public const string OpeningPhoto = "Opening the photo.";

    public static string Filed(string name) => $"{name} filed";

    public static string Deleted(string name) => $"{name} deleted";

    /// <summary>Under a photo (mock: "Robin · 1 h"): who sent it in a group, and when.</summary>
    public static string Caption(string? from, string when) => from is null ? when : $"{from} · {when}";

    /// <summary>A photo as TalkBack reads it.</summary>
    public static string Photo(string? from, string when) => from is null ? $"Photo, {when}" : $"Photo from {from}, {when}";

    // ---- B6: a new text from the Rolodex (W-5.1.2) ----

    /// <summary>People waiting's way to start a text, where the mockup draws Mail; and the Rolodex screen's name.</summary>
    public const string NewText = "New text";

    /// <summary>Search the Rolodex by dictating a name (A10: "by voice" is the name said into the search).</summary>
    public const string SayAName = "Say a name";

    /// <summary>Leave the search: the whole Rolodex again.</summary>
    public const string Everyone = "Everyone";

    /// <summary>The system dictation's prompt for a name.</summary>
    public const string WhoTo = "Who to?";

    /// <summary>The Rolodex, asked for.</summary>
    public const string OpeningContacts = "Opening your contacts.";

    /// <summary>The phone answered with a Rolodex that holds nobody Post can text.</summary>
    public const string NoContacts = "There’s nobody in your phone’s contacts.";

    /// <summary>A search that found nobody: the phone's own Rolodex says the same.</summary>
    public const string NoOneByThatName = "No one by that name.";

    // ---- B7: the call note (W-5.1.5; mock: After the call) and the mail lines (W-5.1.6; mock: Mail) ----

    /// <summary>After the call's one action: dictate a note.</summary>
    public const string AddANote = "Add a note";

    /// <summary>After the call's quieter choice: leave it.</summary>
    public const string NotNow = "Not now";

    /// <summary>Over the note read back, and the dictation's prompt: the phone's own call note says the same.</summary>
    public const string NoteOnThisCall = "Note on this call";

    /// <summary>The note read back is kept: the phone's own word for it.</summary>
    public const string Keep = "Keep";

    /// <summary>A note on its way to the phone.</summary>
    public const string Keeping = "Keeping the note.";

    /// <summary>A typed note past what one message from the wrist may carry.</summary>
    public const string NoteTooLong = "That’s too long for one note.";

    /// <summary>People waiting's way to the mail lines, where the mockup draws it, and the Mail screen's name.</summary>
    public const string Mail = "Mail";

    /// <summary>The mail lines, asked for.</summary>
    public const string FindingMail = "Finding your mail.";

    /// <summary>The phone has no mail from people waiting.</summary>
    public const string NoMail = "No mail waiting.";

    /// <summary>A mail line whose mail has no subject.</summary>
    public const string NoSubject = "No subject";

    /// <summary>After the call's name (mock: "Call with Robin"); a caller the phone's book has no name for is a call.</summary>
    public static string CallWith(string? name) => name is null ? "A call" : $"Call with {name}";

    /// <summary>Under it (mock: "6 min, just now"): how long, when known, then when.</summary>
    public static string CallLine(TimeSpan? length, string when) =>
        length is { } known ? $"{Length(known)}, {when}" : char.ToUpperInvariant(when[0]) + when[1..];

    /// <summary>A call's length in the watch's short words: "Under a minute", "6 min", "1 h 15 min".</summary>
    public static string Length(TimeSpan length)
    {
        if (length < TimeSpan.FromMinutes(1))
        {
            return "Under a minute";
        }
        var minutes = (int)length.TotalMinutes;
        if (minutes < 60)
        {
            return $"{minutes} min";
        }
        return minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";
    }

    /// <summary>A mail line as TalkBack reads it: what, from whom, when.</summary>
    public static string MailLine(MailRow row) => $"{row.Subject}, from {row.From}, {row.When}";

    // ---- B8: the People waiting tile (W-5.1.7; mock: Tile: People waiting) ----

    /// <summary>The tile's name, in the watch's own list of tiles: the mockup's.</summary>
    public const string PeopleWaiting = "People waiting";

    /// <summary>The tile with nobody waiting, as TalkBack reads it (the tile itself is the faint dash and nothing else).</summary>
    public const string NobodyWaiting = "Nobody’s waiting.";

    /// <summary>The tile as TalkBack reads it: the names it shows, newest first, and never how many there are.</summary>
    public static string Waiting(IReadOnlyList<string> names) => names.Count switch
    {
        0 => NobodyWaiting,
        1 => $"{names[0]} is waiting.",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]} are waiting.",
    };

    public static string For(DashTrouble trouble) => trouble switch
    {
        DashTrouble.NoPhone => "Your phone isn’t in reach.",
        DashTrouble.NoAnswer => "Your phone didn’t answer.",
        DashTrouble.Unreadable => "This watch can’t read what your phone sent.",
        DashTrouble.NoInput => "This watch can’t take a reply right now.",
        DashTrouble.Changing => "Your phone’s contacts kept changing.",
        _ => throw new ArgumentOutOfRangeException(nameof(trouble), trouble, "Not a trouble Dash knows."),
    };

    /// <summary>How a send went, as the quiet line under the words; nothing at all when it went.</summary>
    public static string? For(DashSendOutcome outcome) => outcome switch
    {
        DashSendOutcome.Sent => null,
        DashSendOutcome.NoRoute => "Your phone can’t send a text right now.",
        DashSendOutcome.Nowhere => "There’s nobody to send this to.",
        DashSendOutcome.Failed => "The text didn’t go.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not an outcome Post sends."),
    };

    /// <summary>A row as TalkBack reads it: who, whether it is new, when, and the line.</summary>
    public static string Row(PersonRow row) =>
        row.Unread ? $"{row.Name}, new, {row.When}: {row.Line}" : $"{row.Name}, {row.When}: {row.Line}";
}
