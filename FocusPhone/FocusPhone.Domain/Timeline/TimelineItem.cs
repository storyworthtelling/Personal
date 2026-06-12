namespace FocusPhone.Domain.Timeline;

public enum TimelineItemKind
{
    Call,
    Message,
    Email,
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
