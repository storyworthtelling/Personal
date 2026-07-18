namespace FocusPhone.Domain.Timeline;

public enum TimelineItemKind
{
    // Persisted as its int value in the timeline cache: append only, never reorder.
    Call,
    Message,
    Email,
    Voicemail,
}

/// <summary>
/// A reference to a photo or file carried by a message or email. The bytes live
/// on disk (LocalPath) once fetched; before that only the metadata is known.
/// </summary>
public record AttachmentRef(
    string Id,
    string? FileName,
    string MimeType,
    long SizeBytes,
    string? LocalPath = null)
{
    public bool IsImage => MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One communication event, normalized across channels. The six positional
/// members are the original universal shape; everything channel-specific is
/// carried by the optional init members so existing construction sites (and the
/// cache's older rows) stay valid. Email splits Subject/Body out of Snippet;
/// group conversations carry a GroupId + Participants; the bridge carries an
/// ExternalConversationId used to reply to and delete a text.
/// </summary>
public record TimelineItem(
    TimelineItemKind Kind,
    string ContactKey,
    DateTimeOffset TimestampUtc,
    string? Snippet,
    bool Incoming,
    string SourceId)
{
    // Email specifics (null/empty for other kinds).
    public string? Subject { get; init; }
    public string? Body { get; init; }
    public string? FromAddress { get; init; }
    public IReadOnlyList<string> Recipients { get; init; } = [];
    public IReadOnlyList<string> CarbonCopy { get; init; } = [];
    public string? MessageId { get; init; }
    public string? InReplyTo { get; init; }

    // Group conversations: a non-null GroupId threads items by the group rather
    // than by a single person. Participants are the group's member contact keys.
    public string? GroupId { get; init; }
    public IReadOnlyList<string> Participants { get; init; } = [];

    // The Messages-for-Web conversation this text belongs to; the send/delete
    // target for the gmbridge path.
    public string? ExternalConversationId { get; init; }

    public bool Unread { get; init; }
    public IReadOnlyList<AttachmentRef> Attachments { get; init; } = [];

    public bool IsGroup => GroupId is not null;
}

/// <summary>
/// One person's or group's merged history. ContactKey is the representative
/// key (a person's canonical identity, or the group id); MemberKeys lists every
/// normalized key that resolved into this thread so the UI can offer each
/// available channel and resolve a display name from any of them.
/// </summary>
public record ContactThread(
    string ContactKey,
    IReadOnlyList<TimelineItem> Items,
    DateTimeOffset LastActivityUtc)
{
    public IReadOnlyList<string> MemberKeys { get; init; } = [];
    public IReadOnlyList<string> Participants { get; init; } = [];
    public bool IsGroup { get; init; }
    public bool HasUnread { get; init; }
}
