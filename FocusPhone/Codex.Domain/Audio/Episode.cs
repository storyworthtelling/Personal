namespace Codex.Domain.Audio;

/// <summary>
/// One episode of a subscribed show. <see cref="Description"/> and
/// <see cref="ImageUrl"/> are optional because plenty of feeds publish neither;
/// both are trailing so an older cached payload still deserializes.
/// </summary>
public record Episode(
    string Id,
    string FeedId,
    string Title,
    DateTimeOffset PublishedUtc,
    TimeSpan? Duration = null,
    string? AudioUrl = null,
    string? Description = null,
    string? ImageUrl = null);
