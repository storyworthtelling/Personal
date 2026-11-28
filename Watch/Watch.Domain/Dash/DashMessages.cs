namespace Watch.Domain.Dash;

/// <summary>
/// A message on one of Dash's contracts (plan §2.5, P14 W-4.1.1): Post on the phone and Dash on the watch, over the
/// Data Layer. Each knows the path it travels on; <see cref="DashCodec"/> writes every one with an integer <c>v</c> and
/// refuses one it cannot read. Names, lines and message words cross on <c>dash/people</c>, <c>dash/thread</c> and
/// <c>dash/mail</c> only, by design, and a text's or a note's words on <c>dash/send</c> and <c>dash/call-note</c>; the
/// log lines (<see cref="DashLogLines"/>) never carry any of them.
/// </summary>
public abstract record DashMessage
{
    /// <summary>The Data Layer path this message travels on (<see cref="WatchIdentity"/>).</summary>
    public abstract string Path { get; }
}

// ---- dash/people ----

/// <summary>Dash asking who is waiting.</summary>
public sealed record DashPeopleAsk : DashMessage
{
    public override string Path => WatchIdentity.DashPeoplePath;
}

/// <summary>
/// One correspondence waiting (WD §11: a person is their name and one line). <paramref name="Key"/> is opaque
/// (<see cref="DashIds.Correspondence"/>), so no number or address is the key the watch holds or sends back;
/// <paramref name="At"/> is when the newest message came, an instant the watch says as a relative time. There is no
/// photo: photos come on request only.
/// </summary>
public sealed record DashPerson(string Key, string Name, string Line, DateTimeOffset At, bool Unread, bool Group);

/// <summary>Post's answer: the correspondences waiting, newest first.</summary>
public sealed record DashPeople(IReadOnlyList<DashPerson> People) : DashMessage
{
    public override string Path => WatchIdentity.DashPeoplePath;
}

// ---- dash/thread ----

/// <summary>Dash asking for one correspondence's last messages.</summary>
public sealed record DashThreadAsk(string Key) : DashMessage
{
    public override string Path => WatchIdentity.DashThreadPath;
}

/// <summary>What a message in a conversation is.</summary>
public enum DashMessageType
{
    Text,
    Call,
    Voicemail,

    /// <summary>The owner's own note on a call.</summary>
    Note,
}

/// <summary>What an attachment is, so the watch can say "Photo" before it asks for one.</summary>
public enum DashAttachmentType
{
    Photo,
    Video,
    File,
}

/// <summary>An attachment, named by an opaque id (<see cref="DashIds.Attachment"/>); the bytes come on request only.</summary>
public sealed record DashAttachment(string Id, DashAttachmentType Type);

/// <summary>
/// One message of a conversation. <paramref name="Id"/> is opaque (<see cref="DashIds.Message"/>), and is what a file or
/// a delete names; <paramref name="From"/> is the sender's name for an incoming message in a group, and null otherwise.
/// </summary>
public sealed record DashThreadMessage(
    string Id, DateTimeOffset At, bool Incoming, DashMessageType Type, string Text, string? From, IReadOnlyList<DashAttachment> Attachments);

/// <summary>Post's answer: a correspondence's title, its members' names for a group, and its last messages, oldest first.</summary>
public sealed record DashThread(string Key, string Title, bool Group, IReadOnlyList<string> Members, IReadOnlyList<DashThreadMessage> Messages)
    : DashMessage
{
    public override string Path => WatchIdentity.DashThreadPath;
}

// ---- dash/send ----

/// <summary>
/// A text from the wrist: a reply into a correspondence (<paramref name="Key"/>, a person or a group) or a new text to
/// numbers (<paramref name="To"/>), never both. Dictated or typed is the same on the wire. <paramref name="Id"/> is the
/// watch's own name for the send, which Post's answer carries back.
/// </summary>
public sealed record DashSend(string Id, string? Key, IReadOnlyList<string> To, string Text) : DashMessage
{
    public override string Path => WatchIdentity.DashSendPath;

    /// <summary>
    /// A new text to one of the Rolodex's contacts, by its id (<see cref="DashIds.Contact"/>; P14 B6), in place of a
    /// correspondence or numbers: Post maps the id back to the number on the phone, so no number is the wrist's to hold.
    /// </summary>
    public string? Contact { get; init; }
}

/// <summary>How a send from the wrist went.</summary>
public enum DashSendOutcome
{
    /// <summary>A route took it: Google Messages, a plain text or one MMS to a group.</summary>
    Sent,

    /// <summary>Nothing can carry a text right now: no bridge and no permission to send a plain one.</summary>
    NoRoute,

    /// <summary>Nobody to send it to: an unknown correspondence, one with no number, or a group Post cannot reach whole.</summary>
    Nowhere,

    /// <summary>The route Post chose refused it.</summary>
    Failed,
}

/// <summary>Post's answer to a send.</summary>
public sealed record DashSent(string Id, DashSendOutcome Outcome) : DashMessage
{
    public override string Path => WatchIdentity.DashSendPath;
}

// ---- dash/file, dash/delete, dash/undo ----

/// <summary>
/// File a whole correspondence (<paramref name="Key"/>) or some of its messages (<paramref name="Messages"/>, their ids),
/// never both. <paramref name="Window"/> is the undo window the wrist is showing, so Post holds the act at least that long.
/// </summary>
public sealed record DashFile(string Id, string? Key, IReadOnlyList<string> Messages, TimeSpan Window) : DashMessage
{
    public override string Path => WatchIdentity.DashFilePath;
}

/// <summary>Delete a whole correspondence or some of its messages, undo-first; as <see cref="DashFile"/>.</summary>
public sealed record DashDelete(string Id, string? Key, IReadOnlyList<string> Messages, TimeSpan Window) : DashMessage
{
    public override string Path => WatchIdentity.DashDeletePath;
}

/// <summary>Take back the file or delete the watch named <paramref name="Act"/>, inside its window.</summary>
public sealed record DashUndo(string Act) : DashMessage
{
    public override string Path => WatchIdentity.DashUndoPath;
}

// ---- dash/read ----

/// <summary>A correspondence read on the watch, through the newest message the watch showed (<paramref name="Through"/>).</summary>
public sealed record DashRead(string Key, DateTimeOffset Through) : DashMessage
{
    public override string Path => WatchIdentity.DashReadPath;
}

// ---- dash/call-note ----

/// <summary>
/// The owner's note on a call: the call as a message id from a conversation (<paramref name="Call"/>), or the number of a
/// call the watch's own phone app had (<paramref name="Number"/>), never both. The wrist always names a call Post named
/// (<see cref="CallNoteAttempt"/>, P14 B7) and never a number, since it reads none of its own.
/// </summary>
public sealed record DashCallNote(string? Call, string? Number, string Text) : DashMessage
{
    public override string Path => WatchIdentity.DashCallNotePath;

    /// <summary>
    /// The moment the note was written on the wrist (B7), which Post names the note by, so a note told again after a lost
    /// answer is kept once. Absent from a note written before B7: Post names it by when it arrived.
    /// </summary>
    public DateTimeOffset? At { get; init; }
}

/// <summary>
/// Dash asking which call ended at <paramref name="Ended"/>, the moment the watch heard its own call state go idle (B7,
/// W-5.1.5). The watch knows nothing else about the call: the Phone app rang and carried it.
/// </summary>
public sealed record DashCallAsk(DateTimeOffset Ended) : DashMessage
{
    public override string Path => WatchIdentity.DashCallNotePath;
}

/// <summary>
/// Post's answer (B7): the call in its log that ended then, by its message id (<paramref name="Call"/>), with the caller's
/// name from the phone's address book (null when it has none of its own: never a number) and how long it was connected;
/// or, with <paramref name="Call"/> null, that Post has no such call, so there is nothing for a note to go on.
/// </summary>
public sealed record DashCall(DateTimeOffset Ended, string? Call, string? Name, TimeSpan? Length) : DashMessage
{
    public override string Path => WatchIdentity.DashCallNotePath;
}

// ---- dash/mail ----

/// <summary>Dash asking for the mail lines.</summary>
public sealed record DashMailAsk : DashMessage
{
    public override string Path => WatchIdentity.DashMailPath;
}

/// <summary>One mail line: subject and sender only (A11), and when it came.</summary>
public sealed record DashMailLine(string Id, string From, string Subject, DateTimeOffset At);

/// <summary>Post's answer: people-only mail waiting, newest first.</summary>
public sealed record DashMail(IReadOnlyList<DashMailLine> Mail) : DashMessage
{
    public override string Path => WatchIdentity.DashMailPath;
}

// ---- dash/photo (P14 B5, W-5.1.3) ----

/// <summary>Dash asking for one photo of a conversation, by its attachment id (<see cref="DashIds.Attachment"/>).</summary>
public sealed record DashPhotoAsk(string Id) : DashMessage
{
    public override string Path => WatchIdentity.DashPhotoPath;
}

/// <summary>
/// Post's answer: the photo, shrunk and re-encoded on the phone as a JPEG of at most <see cref="DashLimits.PhotoBytes"/>
/// (<see cref="PhotoFit"/>), so it carries none of the picture's own metadata. Held in memory on the wrist, never written down.
/// </summary>
public sealed record DashPhoto(string Id, byte[] Jpeg) : DashMessage
{
    public override string Path => WatchIdentity.DashPhotoPath;
}

// ---- dash/rolodex (P14 B6, W-5.1.2) ----

/// <summary>Dash asking for one page of the phone's Rolodex, from the first (0).</summary>
public sealed record DashRolodexAsk(int Page) : DashMessage
{
    public override string Path => WatchIdentity.DashRolodexPath;
}

/// <summary>
/// One contact in the phone's Rolodex (the owner's ruling, B6): its name and an opaque id (<see cref="DashIds.Contact"/>),
/// and nothing else: never a number, an address or a photo. A new text names the id; Post maps it back to the number.
/// </summary>
public sealed record DashContact(string Id, string Name);

/// <summary>
/// Post's answer: one page of the Rolodex, in Post's own order (by name). <paramref name="Pages"/> says how many there
/// are, and <paramref name="Edition"/> (<see cref="DashIds.Edition"/>) which state of the book they were cut from, so the
/// wrist gathers pages that belong together and starts again when the book changed under it.
/// </summary>
public sealed record DashRolodex(int Page, int Pages, string Edition, IReadOnlyList<DashContact> Contacts) : DashMessage
{
    public override string Path => WatchIdentity.DashRolodexPath;
}

// ---- dash/arrived (P14 B8) ----

/// <summary>
/// Post telling the wrist a text just arrived that the phone itself alerts for (a spam text never does), so the wrist can
/// give its two short taps (WD §7, A12): the correspondence by its id (<see cref="DashIds.Correspondence"/>, the key People
/// waiting shows it under), when the phone had it (so the wrist can tell a telling that crossed too late), and whether its
/// sender is a favourite, starred in the phone's contacts (A12's overnight rule). Never a name, a number or a word: the wrist needs none of them to buzz.
/// </summary>
public sealed record DashArrived(string Key, DateTimeOffset At, bool Favourite) : DashMessage
{
    public override string Path => WatchIdentity.DashArrivedPath;
}
