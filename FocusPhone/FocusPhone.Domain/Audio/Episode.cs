namespace FocusPhone.Domain.Audio;

public record Episode(
    string Id,
    string FeedId,
    string Title,
    DateTimeOffset PublishedUtc,
    TimeSpan? Duration = null);
