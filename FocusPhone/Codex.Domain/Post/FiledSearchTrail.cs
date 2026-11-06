using System.Globalization;

namespace Codex.Domain.Post;

/// <summary>Tokens one search call used, as the API reports them.</summary>
public sealed record FiledUsage(int Input, int CacheRead, int CacheWrite, int Output);

/// <summary>
/// One ask of a Filed search, in numbers. <see cref="WordsAgree"/> is how many of Claude's picks
/// the word match also ranks near the top; <see cref="WordsSkipped"/> is how many entries hold
/// every word of the query yet were neither picked nor already shown. Many of the second is the
/// clearest sign in the log that Claude missed what the owner named.
/// </summary>
public sealed record FiledRound(
    int Round,
    bool ByClaude,
    string? WhyWords,
    int QueryWords,
    int Sent,
    int Total,
    int Picked,
    int Strong,
    int Rejected,
    int WordsAgree,
    int WordsSkipped,
    long Milliseconds,
    FiledUsage? Usage,
    string? StopReason,
    string? RequestId);

/// <summary>How a search ended: a result put back or opened, a new search, or leaving Filed.</summary>
public enum FiledSearchEnd
{
    PutBack,
    Opened,
    NewSearch,
    Left,
}

/// <summary>
/// The log lines that say whether Filed search is finding things (LOG-LOOP-PLAN.md wording rules:
/// fixed words, so the weekly digest can group them). Every ask logs a round line; every search
/// logs one verdict from what the owner did next:
/// <list type="bullet">
/// <item><b>HIT #k</b>: a result was opened or put back; k is its place among everything shown,
/// so a HIT past 5 means the first five missed.</item>
/// <item><b>REPHRASED</b>: a new search with nothing used — the plainest miss.</item>
/// <item><b>EMPTY</b>: nothing was found at all.</item>
/// <item><b>LEFT</b>: Filed was left with nothing used. Not counted as a miss: seeing a result
/// may have been all the owner needed.</item>
/// </list>
/// No line carries the query, a name or anything filed — only counts, ranks, times and tokens.
/// </summary>
public static class FiledSearchTrail
{
    public static string Round(FiledRound round)
    {
        var how = round.ByClaude ? "claude" : $"words ({round.WhyWords})";
        var line = $"FILED SEARCH round {round.Round} by {how}: ";
        if (!round.ByClaude)
        {
            return line + $"{round.Picked} found; query {round.QueryWords} words; {round.Milliseconds} ms";
        }

        line += $"{round.Picked} picked ({round.Strong} strong), {round.Rejected} rejected; "
            + $"words agree on {round.WordsAgree}, {round.WordsSkipped} full word matches skipped; "
            + $"sent {round.Sent} of {round.Total}; query {round.QueryWords} words; {round.Milliseconds} ms";
        if (round.Usage is { } usage)
        {
            line += string.Create(CultureInfo.InvariantCulture,
                $"; tokens in {usage.Input}, cache read {usage.CacheRead}, cache write {usage.CacheWrite}, out {usage.Output}");
        }
        if (round.StopReason is { } stop)
        {
            line += $"; stop {stop}";
        }
        if (round.RequestId is { } id)
        {
            line += $"; request {id}";
        }
        return line;
    }

    /// <summary>The verdict, or null for a search that was never asked.</summary>
    public static string? Ended(FiledSearchSession session, string? used, FiledSearchEnd how)
    {
        if (session.Round == 0)
        {
            return null;
        }
        var tally = $"{session.Round} {(session.Round == 1 ? "round" : "rounds")}, {session.Shown.Count} shown";

        if (used is not null && session.Rank(used) is { } rank)
        {
            var what = how == FiledSearchEnd.PutBack ? "put back" : "opened";
            return $"FILED SEARCH HIT #{rank} (round {session.RoundOf(used)}): {what}; {tally}";
        }
        if (session.Shown.Count == 0)
        {
            return $"FILED SEARCH EMPTY: nothing found; {tally}";
        }
        if (how == FiledSearchEnd.NewSearch)
        {
            return $"FILED SEARCH REPHRASED: nothing used; {tally}";
        }
        var ranOut = session.Exhausted ? ", ran out of results" : string.Empty;
        return $"FILED SEARCH LEFT: nothing used{ranOut}; {tally}";
    }
}
