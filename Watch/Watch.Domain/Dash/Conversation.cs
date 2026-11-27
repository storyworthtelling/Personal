namespace Watch.Domain.Dash;

/// <summary>
/// One message as the conversation draws it (mock: A conversation): the owner's on the right in Dash's quieter glass,
/// everyone else's on the left on the card, and in a group the sender's name over theirs. A message's photos are drawn
/// as the mockup's picture (mock: A group text with a photo), over its words if it has any of its own.
/// </summary>
public sealed record Bubble(string Id, bool Mine, string Words, string? From, DateTimeOffset At)
{
    /// <summary>The message's photos by attachment id, in Post's order (W-5.1.3): each drawn as the mockup's picture.</summary>
    public IReadOnlyList<string> Photos { get; init; } = [];

    /// <summary>Whether the message has words of its own to draw in a bubble; a photo with none is the picture alone.</summary>
    public bool Said { get; init; } = true;

    // Two answers that say the same are the same conversation: the photos are compared by what they hold, not by list.
    public bool Equals(Bubble? other) =>
        other is not null
        && Id == other.Id && Mine == other.Mine && Words == other.Words && From == other.From && At == other.At && Said == other.Said
        && Photos.SequenceEqual(other.Photos, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Id, Mine, Words, From, At, Said, Photos.Count);
}

/// <summary>A reply the wrist sent and Post answered as sent, shown until Post's own copy of it comes in a conversation.</summary>
public sealed record SentHere(string Id, string Words, DateTimeOffset At);

/// <summary>
/// A conversation on the wrist (W-5.1.1, W-5.1.2; A10, A14): Post's last messages, oldest first and the newest by the
/// Reply pill. Reading it on the wrist reads it on the phone, through the newest message the wrist drew (B1's
/// <c>dash/read</c>), never "now", so a text that arrives after the wrist drew it stays unread on both. A reply the wrist
/// sent shows at once and gives way to Post's copy when a later answer carries it.
/// </summary>
public static class Conversation
{
    /// <summary>How far behind the watch's clock the phone's stamp on a sent text may be and still be that text.</summary>
    public static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<Bubble> Bubbles(DashThread thread, IReadOnlyList<SentHere> sentHere)
    {
        var bubbles = thread.Messages
            .OrderBy(message => message.At)
            .Select(message => new Bubble(message.Id, !message.Incoming, WordsOf(message), thread.Group && message.Incoming ? message.From : null, message.At)
            {
                Photos = PhotosOf(message),
                Said = PhotosOf(message).Count == 0 || HasWordsOfItsOwn(message),
            })
            .ToList();
        bubbles.AddRange(StillOwed(thread, sentHere).Select(sent => new Bubble(sent.Id, true, sent.Words, null, sent.At)));
        return bubbles;
    }

    /// <summary>The replies sent here that the conversation does not yet carry as the owner's own text.</summary>
    public static IReadOnlyList<SentHere> StillOwed(DashThread thread, IReadOnlyList<SentHere> sentHere) =>
        sentHere
            .Where(sent => !thread.Messages.Any(message =>
                !message.Incoming
                && message.At >= sent.At - ClockSlack
                && string.Equals(message.Text.Trim(), sent.Words.Trim(), StringComparison.Ordinal)))
            .ToList();

    /// <summary>The read the wrist tells Post once it has drawn the conversation, or none when there is nothing in it.</summary>
    public static DashRead? ReadFor(DashThread thread) =>
        thread.Messages.Count == 0 ? null : new DashRead(thread.Key, thread.Messages.Max(message => message.At));

    /// <summary>The photos still to fetch, newest message first, so the ones by the Reply pill come first (B5).</summary>
    public static IReadOnlyList<string> PhotosToAsk(IReadOnlyList<Bubble> bubbles, Func<string, bool> held) =>
        bubbles.Reverse().SelectMany(bubble => bubble.Photos).Where(id => !held(id)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The one action (mock): Reply, or Reply to all in a group.</summary>
    public static string ReplyLabel(DashThread thread) => thread.Group ? DashWords.ReplyToAll : DashWords.Reply;

    private static IReadOnlyList<string> PhotosOf(DashThreadMessage message) =>
        message.Attachments.Where(attachment => attachment.Type == DashAttachmentType.Photo).Select(attachment => attachment.Id).ToList();

    // Words beside a photo, not the word that only says what it is ("Photo", from a phone that wrote its stand-in).
    private static bool HasWordsOfItsOwn(DashThreadMessage message) =>
        !string.IsNullOrWhiteSpace(message.Text)
        && !string.Equals(message.Text.Trim(), WordsOf(message with { Text = string.Empty }), StringComparison.Ordinal);

    // Post words every message its own way ("Missed call", "Photo"); a message that crossed with none is said by what it is.
    private static string WordsOf(DashThreadMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            return message.Text;
        }
        return message.Type switch
        {
            DashMessageType.Call => "Call",
            DashMessageType.Voicemail => "Voicemail",
            _ => message.Attachments.FirstOrDefault()?.Type switch
            {
                DashAttachmentType.Photo => "Photo",
                DashAttachmentType.Video => "Video",
                DashAttachmentType.File => "Attachment",
                _ => string.Empty,
            },
        };
    }
}
