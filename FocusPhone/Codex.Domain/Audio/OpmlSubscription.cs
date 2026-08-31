namespace Codex.Domain.Audio;

/// <summary>A subscription read from an OPML file: its display title and feed URL.</summary>
public record OpmlSubscription(string Title, string FeedUrl);
