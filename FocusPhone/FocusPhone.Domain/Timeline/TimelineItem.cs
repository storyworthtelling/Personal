namespace FocusPhone.Domain.Timeline;

public enum TimelineItemKind
{
    // Persisted as its int value in the timeline cache: append only, never reorder.
    Call,
    Message,
    Email,
    Voicemail,
}

public record TimelineItem(
    TimelineItemKind Kind,
    string ContactKey,
    DateTimeOffset TimestampUtc,
    string? Snippet,
    bool Incoming,
    string SourceId);

public record ContactThread(
    string ContactKey,
    IReadOnlyList<TimelineItem> Items,
    DateTimeOffset LastActivityUtc);
