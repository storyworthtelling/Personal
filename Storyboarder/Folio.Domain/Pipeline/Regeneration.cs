using System.Globalization;
using Folio.Domain.Series;

namespace Folio.Domain.Pipeline;

/// <summary>A book that can be regenerated (release R): its latest finished draft, and what places it.</summary>
public sealed record RegenerationCandidate(
    Guid DraftId, string Title, int DraftNumber, int WordCount, Guid? SeriesId, int SeriesSequence, StoryPlacement? Placement);

/// <summary>A book queued for regeneration, with its rank in its series' story order (zero for a standalone book).</summary>
public sealed record RegenerationItem(RegenerationCandidate Book, int Rank);

/// <summary>
/// Which order the books are regenerated in (release R). A series goes in story order, so each book's bible
/// is rebuilt before the next book is read against it; a book waits for every book ranked before it in its
/// series. Standalone books have no order to keep.
/// </summary>
public static class RegenerationPlan
{
    public static IReadOnlyList<RegenerationItem> Order(IReadOnlyList<RegenerationCandidate> books)
    {
        var items = new List<RegenerationItem>();
        foreach (var group in books.GroupBy(book => book.SeriesId))
        {
            if (group.Key is null)
            {
                items.AddRange(group.Select(book => new RegenerationItem(book, 0)));
                continue;
            }

            var story = StoryOrder.Sort([.. group.Select(book => (book.Title, book.SeriesSequence, book.Placement))]);
            items.AddRange(group
                .OrderBy(book => story.ToList().IndexOf(book.Title))
                .Select((book, rank) => new RegenerationItem(book, rank)));
        }
        return items;
    }
}

/// <summary>
/// A series' books in story order (R21): the order they were written, with each placed book moved after,
/// before or between the books its placement names. A book set in an era of its own keeps its written place.
/// </summary>
public static class StoryOrder
{
    public static IReadOnlyList<string> Sort(IReadOnlyList<(string Title, int Written, StoryPlacement? Placement)> books)
    {
        var order = books.OrderBy(book => book.Written).Select(book => book.Title).ToList();
        foreach (var (title, _, placement) in books.OrderBy(book => book.Written))
        {
            if (placement is null || placement.Kind == PlacementKind.Era)
                continue;
            var anchor = order.IndexOf(placement.Book);
            if (anchor < 0 || placement.Book == title)
                continue;

            order.Remove(title);
            anchor = order.IndexOf(placement.Book);
            order.Insert(placement.Kind == PlacementKind.Before ? anchor : anchor + 1, title);
        }
        return order;
    }
}

/// <summary>
/// What regenerating a book roughly costs in model calls (release R). The reads are the measured part: $3.38
/// for a 90,000-word draft without batching. The reductions, scene judgements, round table and series reads
/// beside them are an estimate, and most of it runs at the batch discount. Pictures for changed scenes are
/// not counted; unchanged scenes keep theirs. Shown so the owner approves a spend before it starts.
/// </summary>
public static class RegenerationCost
{
    public const decimal MeasuredReadsPer90k = 3.38m;
    public const decimal OtherCallsPer90k = 2.00m;
    public const decimal BatchedShare = 0.6m;

    public static decimal Estimate(int words) =>
        Math.Round((MeasuredReadsPer90k + OtherCallsPer90k) * Math.Max(words, 1_000) / 90_000m * BatchedShare, 2);
}

/// <summary>The paper card that asks before anything is spent (release R).</summary>
public sealed record RegenerationCard(string Title, string Subtitle, string? Action)
{
    public static RegenerationCard For(IReadOnlyList<RegenerationItem> books)
    {
        if (books.Count == 0)
            return new RegenerationCard("No reviewed books to regenerate", "Every book Folio has finished is listed here once it has a report.", null);

        var total = books.Sum(item => RegenerationCost.Estimate(item.Book.WordCount));
        var dollars = Math.Ceiling(total).ToString("N0", CultureInfo.InvariantCulture);
        var titles = string.Join(", ", books.Select(item => item.Book.DraftNumber > 1
            ? $"{item.Book.Title} (draft {item.Book.DraftNumber})"
            : item.Book.Title));
        var noun = books.Count == 1 ? "book" : "books";
        return new RegenerationCard(
            $"Regenerate {books.Count} reviewed {noun}?",
            $"{titles}. About ${dollars} in model calls, a rough estimate. Each report is rewritten in the new form and "
            + "replaces the one in Drive; nothing is sent to the reMarkable, and your voice profile is not touched.",
            $"Regenerate {books.Count} {noun} (about ${dollars})");
    }
}
