using Codex.Domain.Timeline;

namespace Codex.Infrastructure.Email;

/// <summary>
/// Which Gmail ids this phone already holds, and as which source id (the RFC Message-Id the cache keys mail by), so
/// a sync fetches only messages it has never seen (efficiency audit §2). Post keeps it in <c>timeline.db</c>.
/// </summary>
public interface IGmailIdIndex
{
    string? SourceIdFor(string gmailId);

    void Remember(string gmailId, string sourceId);
}

/// <summary>
/// The mail window as Gmail holds it: the messages fetched because they were new to this phone, every source id
/// still in the window (new and known), and Gmail's history id at the start of the listing.
/// </summary>
public sealed record GmailMirror(IReadOnlyList<TimelineItem> Fetched, IReadOnlyList<string> Present, string? HistoryId);

/// <summary>What Gmail's history says was added to the inbox or sent since a history id, and the id to ask from next.</summary>
public sealed record GmailChanges(IReadOnlyList<TimelineItem> Fetched, string? HistoryId);
