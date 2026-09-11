using Foreword.Domain.Brief;

namespace Foreword.Domain.Interfaces;

/// <summary>The xkcd strips the owner has not seen, fetched with their images.</summary>
public interface IComicClient
{
    /// <summary>The newest strip's number, or null when nobody could be asked.</summary>
    Task<int?> Latest();

    /// <summary>One strip with its image, or null when it could not be fetched.</summary>
    Task<ComicStrip?> Get(int number);
}

/// <summary>
/// What happened on this date (Wikipedia's keyless on-this-day feed, F15).
/// </summary>
public interface IHistoryClient
{
    /// <summary>
    /// The notes for a date, newest year first as the feed returns them. Empty when the call failed
    /// — a factoid is the most skippable thing in the document and must never cost the morning.
    /// </summary>
    Task<IReadOnlyList<HistoryNote>> OnThisDay(DateOnly date);
}
