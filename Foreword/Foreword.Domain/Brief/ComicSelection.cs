namespace Foreword.Domain.Brief;

/// <summary>
/// Which comics print (T5: "xkcd prints whatever you have not seen").
///
/// <para>Not "the latest" — whatever was missed. A strip is a thing you read once, so one nobody
/// read is one that was lost rather than one that was old; a week away should bring back the week.
/// But only within reason: a fresh install has seen nothing, and "everything you have not seen"
/// would be three thousand comics.</para>
/// </summary>
public static class ComicSelection
{
    /// <summary>
    /// The comic that does not exist. xkcd skipped the number deliberately — it is the joke — and
    /// the API returns a 404 for it. Asked for, it would print one blank page every morning
    /// forever, because the number never becomes fetchable.
    /// </summary>
    public const int MissingComic = 404;

    public static IReadOnlyList<int> Unseen(int latest, IReadOnlyCollection<int> seen, int max)
    {
        if (latest <= 0 || max <= 0)
        {
            return [];
        }

        // A fresh install seeds itself on the latest rather than printing the whole archive.
        if (seen.Count == 0)
        {
            return [latest];
        }

        // Forward from the highest already printed. A gap further back is not filled: what has
        // been printed is not a list to audit, and an old strip beside today's reads as a mistake.
        var from = seen.Max() + 1;
        if (from > latest)
        {
            return [];
        }

        var candidates = Enumerable.Range(from, latest - from + 1)
            .Where(number => number != MissingComic)
            .ToList();

        // Capped to the NEWEST, because coming back from a month away the strips worth having are
        // the recent ones — and oldest-first within what is kept, since reading them in publication
        // order is the only order that makes sense of a running joke.
        return [.. candidates.Skip(Math.Max(0, candidates.Count - max))];
    }

    /// <summary>
    /// What to remember after a morning. Everything <b>offered</b> is marked seen, not just what
    /// fitted — otherwise the same backlog is offered again tomorrow and the cap becomes a queue
    /// that never drains.
    /// </summary>
    public static int HighWaterMark(int latest, IReadOnlyList<int> printed) =>
        printed.Count == 0 ? latest : Math.Max(latest, printed.Max());
}
