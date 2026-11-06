using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// What the log says about each Filed search, so the weekly read can tell whether Claude is
/// finding things. Each round is logged with its own numbers, and each search ends with a
/// verdict from what the owner did next. No line carries the query or anything filed: only
/// counts, ranks, timings and tokens.
/// </summary>
public class FiledSearchTrailTests
{
    private static FiledRound ClaudeRound() => new(
        Round: 2, ByClaude: true, WhyWords: null, QueryWords: 6, Sent: 840, Total: 900,
        Picked: 3, Strong: 1, Rejected: 1, WordsAgree: 2, WordsSkipped: 4, Milliseconds: 1830,
        Usage: new FiledUsage(210, 31200, 0, 64), StopReason: "end_turn", RequestId: "req_1");

    [Fact]
    public void AClaudeRoundSaysWhatItPickedAgainstTheWordMatch()
    {
        Assert.Equal(
            "FILED SEARCH round 2 by claude: 3 picked (1 strong), 1 rejected; words agree on 2, "
            + "4 full word matches skipped; sent 840 of 900; query 6 words; 1830 ms; "
            + "tokens in 210, cache read 31200, cache write 0, out 64; stop end_turn; request req_1",
            FiledSearchTrail.Round(ClaudeRound()));
    }

    [Fact]
    public void AWordsRoundSaysWhyClaudeWasNotAsked()
    {
        var round = ClaudeRound() with
        {
            Round = 1, ByClaude = false, WhyWords = "no Claude key", Picked = 4, Strong = 0, Rejected = 0,
            Usage = null, StopReason = null, RequestId = null,
        };

        Assert.Equal(
            "FILED SEARCH round 1 by words (no Claude key): 4 found; query 6 words; 1830 ms",
            FiledSearchTrail.Round(round));
    }

    [Theory]
    [InlineData(FiledSearchEnd.PutBack, 7, "FILED SEARCH HIT #7 (round 2): put back; 2 rounds, 10 shown")]
    [InlineData(FiledSearchEnd.Opened, 1, "FILED SEARCH HIT #1 (round 1): opened; 2 rounds, 10 shown")]
    public void ASearchThatFoundSomethingSaysWhereItWas(FiledSearchEnd how, int rank, string expected)
    {
        var session = FiledSearchSession.Start("q")
            .Record(["a", "b", "c", "d", "e"])
            .Record(["f", "g", "h", "i", "j"]);
        var used = session.Shown[rank - 1];

        Assert.Equal(expected, FiledSearchTrail.Ended(session, used, how));
    }

    [Fact]
    public void ARephrasedSearchIsAMiss()
    {
        var session = FiledSearchSession.Start("q").Record(["a", "b"]).Record(["c"]);

        Assert.Equal("FILED SEARCH REPHRASED: nothing used; 2 rounds, 3 shown",
            FiledSearchTrail.Ended(session, used: null, FiledSearchEnd.NewSearch));
    }

    [Fact]
    public void ASearchThatFoundNothingIsEmpty()
    {
        var session = FiledSearchSession.Start("q").Record([]);

        Assert.Equal("FILED SEARCH EMPTY: nothing found; 1 round, 0 shown",
            FiledSearchTrail.Ended(session, used: null, FiledSearchEnd.NewSearch));
    }

    // Leaving without touching a result may mean the owner only needed to see it, so it is
    // logged as its own verdict rather than counted as a miss.
    [Fact]
    public void LeavingWithoutUsingAResultIsItsOwnVerdict()
    {
        var session = FiledSearchSession.Start("q").Record(["a"]).Record([]);

        Assert.Equal("FILED SEARCH LEFT: nothing used, ran out of results; 2 rounds, 1 shown",
            FiledSearchTrail.Ended(session, used: null, FiledSearchEnd.Left));
    }

    [Fact]
    public void ASearchNeverAskedLogsNothing()
    {
        Assert.Null(FiledSearchTrail.Ended(FiledSearchSession.Start("q"), used: null, FiledSearchEnd.Left));
    }
}
