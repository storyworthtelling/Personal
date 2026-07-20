namespace FocusPhone.Domain.Audio;

/// <summary>
/// A subscribed show. The feed URL is the identity — <see cref="RssFeedClient"/>
/// already uses the feed URL as an episode's FeedId, so the two line up without a
/// separate key.
/// </summary>
public record PodcastSubscription(
    string FeedUrl,
    string Title,
    DateTimeOffset AddedUtc,
    int SortOrder);
