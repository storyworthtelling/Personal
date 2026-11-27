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
/// No mockup draws a new text either (B6): its words follow the nearest drawn screens, and the phone's Rolodex.
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
