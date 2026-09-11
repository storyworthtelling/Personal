using Foreword.Domain.Brief;
using Xunit;

namespace Foreword.Tests.Nyt;

/// <summary>
/// Which comics print (T5: "xkcd prints whatever you have not seen").
///
/// <para>Not "the latest" — whatever was missed. A week away and the owner gets the week, because a
/// strip is a thing you read once and a strip nobody read is a strip that was lost rather than one
/// that was old.</para>
/// </summary>
public class ComicSelectionTests
{
    [Fact]
    public void AnOrdinaryMorningPrintsTheOneSinceYesterday()
    {
        Assert.Equal([3297], ComicSelection.Unseen(latest: 3297, seen: [3296], max: 5));
    }

    /// <summary>Nothing new, nothing printed. The brief is shorter rather than repeating itself.</summary>
    [Fact]
    public void NothingNewPrintsNothing()
    {
        Assert.Empty(ComicSelection.Unseen(latest: 3296, seen: [3296], max: 5));
    }

    /// <summary>
    /// A week away prints the week, oldest first — a strip is read once, and reading them in the
    /// order they were published is the only order that makes sense of a running joke.
    /// </summary>
    [Fact]
    public void AWeekAwayPrintsTheWeekOldestFirst()
    {
        Assert.Equal([3294, 3295, 3296, 3297],
            ComicSelection.Unseen(latest: 3297, seen: [3293], max: 5));
    }

    /// <summary>
    /// The first morning prints ONE. A fresh install has seen nothing, and "everything you have not
    /// seen" would be three thousand comics — a brief that took a day to print and a bill for the
    /// bandwidth. So the first run seeds itself on the latest.
    /// </summary>
    [Fact]
    public void TheFirstMorningPrintsOnlyTheLatest()
    {
        Assert.Equal([3297], ComicSelection.Unseen(latest: 3297, seen: [], max: 5));
    }

    /// <summary>
    /// A long absence is capped, and the cap takes the NEWEST rather than the oldest. Coming back
    /// from a month away, the strips worth having are the recent ones; the rest are marked seen so
    /// the backlog does not follow the owner around for a week.
    /// </summary>
    [Fact]
    public void ALongAbsenceIsCappedToTheNewest()
    {
        Assert.Equal([3293, 3294, 3295, 3296, 3297],
            ComicSelection.Unseen(latest: 3297, seen: [3200], max: 5));
    }

    /// <summary>
    /// <b>404 does not exist.</b> xkcd skipped the number deliberately — it is the joke — and the
    /// API returns a 404 for comic 404. A selection that asked for it would print one blank page
    /// forever, because the number never becomes fetchable.
    /// </summary>
    [Fact]
    public void TheComicThatDoesNotExistIsNeverAskedFor()
    {
        Assert.DoesNotContain(404, ComicSelection.Unseen(latest: 406, seen: [402], max: 5));
        Assert.Equal([403, 405, 406], ComicSelection.Unseen(latest: 406, seen: [402], max: 5));
    }

    /// <summary>
    /// A gap in the middle is not filled. If the owner has seen 3297 but somehow not 3295, the
    /// brief moves forward rather than going back — what has been printed is not a list to audit,
    /// and re-printing an old strip beside today's reads as a mistake.
    /// </summary>
    [Fact]
    public void TheBriefMovesForwardRatherThanFillingGaps()
    {
        Assert.Empty(ComicSelection.Unseen(latest: 3297, seen: [3295, 3297], max: 5));
    }

    /// <summary>
    /// Everything offered is remembered, not just what fitted. A capped morning marks the skipped
    /// ones seen too — otherwise the same backlog is offered again tomorrow, and the cap turns into
    /// a queue that never drains.
    /// </summary>
    [Fact]
    public void EverythingPassedOverIsStillRemembered()
    {
        Assert.Equal(3297, ComicSelection.HighWaterMark(latest: 3297, printed: [3293, 3294]));
    }

    [Fact]
    public void AMorningWithNoComicsLeavesTheMarkWhereItWas()
    {
        Assert.Equal(3297, ComicSelection.HighWaterMark(latest: 3297, printed: []));
    }
}
