using Folio.Domain.Analysis;
using Folio.Domain.Reading;
using Folio.Domain.Text;

namespace Folio.Tests.Reading;

/// <summary>
/// The owner's ruling: each of the six readers scores the book from 1 to 10 on its own question, in
/// its own terms, against written anchors so that a six means the same thing from one read to the
/// next, with one sentence saying why. The sections do not score.
/// </summary>
public class ReaderScoreTests
{
    private const int NovelWords = 90_000;
    private const int ShortStoryWords = 1_209;

    [Fact]
    public void EveryReaderHasAnAnchorAtTwoFourSixEightAndTen()
    {
        foreach (var persona in PersonaCatalogue.Readers)
        {
            foreach (var length in Enum.GetValues<StoryLength>())
            {
                var anchors = persona.AnchorsFor(length);
                Assert.Equal(5, anchors.Count);
                Assert.All(anchors, anchor => Assert.False(string.IsNullOrWhiteSpace(anchor)));
                Assert.Equal(5, anchors.Distinct().Count());
            }
        }
    }

    [Fact]
    public void NoTwoReadersShareAnAnchor()
    {
        var all = PersonaCatalogue.Readers.SelectMany(persona => persona.AnchorsFor(StoryLength.Novel)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void SectionsDoNotScore()
    {
        foreach (var persona in PersonaCatalogue.Sections)
        {
            Assert.Empty(persona.AnchorsFor(StoryLength.Novel));
            Assert.DoesNotContain("from 1 to 10", ReadingPrompt.Instruction(persona, NovelWords, GenreFrame.Unknown),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AReaderIsAskedToScoreAgainstItsOwnAnchors()
    {
        foreach (var persona in PersonaCatalogue.Readers)
        {
            var instruction = ReadingPrompt.Instruction(persona, NovelWords, GenreFrame.Unknown);

            Assert.Contains("from 1 to 10", instruction, StringComparison.Ordinal);
            foreach (var (anchor, point) in persona.AnchorsFor(StoryLength.Novel).Zip(new[] { 2, 4, 6, 8, 10 }))
                Assert.Contains($"{point}: {anchor}", instruction, StringComparison.Ordinal);
        }
    }

    /// <summary>On a short story the Agent is scoring the writer's talent, so it scores on talent's anchors.</summary>
    [Fact]
    public void TheAgentScoresAShortStoryOnTheWritersTalent()
    {
        var agent = PersonaCatalogue.ById("agent");
        var shortStory = agent.AnchorsFor(StoryLength.ShortStory);

        Assert.NotEqual(agent.AnchorsFor(StoryLength.Novel), shortStory);
        Assert.Contains(shortStory[^1], ReadingPrompt.Instruction(agent, ShortStoryWords, GenreFrame.Unknown), StringComparison.Ordinal);
        Assert.DoesNotContain(agent.AnchorsFor(StoryLength.Novel)[^1],
            ReadingPrompt.Instruction(agent, ShortStoryWords, GenreFrame.Unknown), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-3)]
    public void AScoreOutsideOneToTenIsAnIncompleteRead(int score) =>
        Assert.NotNull(ReaderRead.Incomplete(Whole() with { Score = score }));

    /// <summary>The omnibus reduce's reviews are unscored by design; only a reader's own pass must score.</summary>
    [Fact]
    public void AnUnscoredOmnibusReviewIsStillWhole() =>
        Assert.True(ReaderRead.IsWhole(Whole() with { Score = 0, ScoreReason = "" }, scored: false));

    [Fact]
    public void AScoreWithNoReasonIsAnIncompleteRead() =>
        Assert.NotNull(ReaderRead.Incomplete(Whole() with { ScoreReason = " " }));

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void AScoreFromOneToTenWithAReasonIsWhole(int score) =>
        Assert.Null(ReaderRead.Incomplete(Whole() with { Score = score }));

    [Theory]
    [InlineData(7, 5, "up from 5 last draft")]
    [InlineData(4, 7, "down from 7 last draft")]
    [InlineData(6, 6, "unchanged from last draft")]
    public void MovementIsSaidAgainstTheLastDraft(int now, int before, string expected) =>
        Assert.Equal(expected, ReaderScores.Movement(now, before));

    [Fact]
    public void NoPreviousScoreMeansNoMovement()
    {
        Assert.Null(ReaderScores.Movement(7, null));
        Assert.Null(ReaderScores.Movement(7, 0));
    }

    [Fact]
    public void CompareIsInReadingOrderAndPairsEachReaderWithItsOwnLastScore()
    {
        var current = new[]
        {
            Whole("agent", 7),
            Whole("developmental-editor", 6),
            Whole("genre-fan", 0),
        };
        var previous = new[] { Whole("agent", 5), Whole("harsh-critic", 3) };

        var scores = ReaderScores.Compare(current, previous);

        Assert.Equal(["developmental-editor", "agent"], scores.Select(score => score.PersonaId));
        Assert.Null(scores[0].Previous);
        Assert.Equal(5, scores[1].Previous);
        Assert.Equal("Hugo Hook", scores[1].Name);
        Assert.Equal("Agent", scores[1].Role);
        Assert.Equal("Could I sell it", scores[1].Question);
    }

    [Fact]
    public void TheScoreLineCarriesTheMovementWhenThereIsOne()
    {
        Assert.Equal("7/10 · up from 5 last draft", ReaderScores.Line(7, 5));
        Assert.Equal("7/10", ReaderScores.Line(7, null));
    }

    /// <summary>
    /// Each reader's scores across drafts, in draft order, for the phone's trend line (R13, R37). A draft
    /// the reader did not score (read before scores existed) is skipped rather than drawn as zero.
    /// </summary>
    [Fact]
    public void HistoryIsEachReadersScoresInDraftOrder()
    {
        var history = ReaderScores.History(
        [
            (1, new[] { Whole("agent", 4), Whole("genre-fan", 0) }),
            (2, new[] { Whole("agent", 6), Whole("genre-fan", 7) }),
            (3, new[] { Whole("agent", 8) }),
        ]);

        Assert.Equal([new DraftScore(1, 4), new DraftScore(2, 6), new DraftScore(3, 8)], history["agent"]);
        Assert.Equal([new DraftScore(2, 7)], history["genre-fan"]);
    }

    /// <summary>The trend line's points: drafts evenly across, a 10 at the top and a 1 at the foot.</summary>
    [Fact]
    public void TheTrendLineSpreadsDraftsAcrossAndScoresUp()
    {
        var points = ScoreTrend.Points([new DraftScore(1, 1), new DraftScore(2, 10), new DraftScore(3, 5)], width: 100, height: 20);

        Assert.Equal(3, points.Count);
        Assert.Equal((0f, 20f), (points[0].X, points[0].Y));
        Assert.Equal((50f, 0f), (points[1].X, points[1].Y));
        Assert.Equal(100f, points[2].X);
        Assert.True(points[2].Y > 0 && points[2].Y < 20);
    }

    [Fact]
    public void AReaderWithOneScoredDraftHasNoTrendLine() =>
        Assert.Empty(ScoreTrend.Points([new DraftScore(1, 6)], width: 100, height: 20));

    [Fact]
    public void AScoreIsWrittenOutOfTen() =>
        Assert.Equal("7/10", ReaderScores.OutOfTen(7));

    private static Review Whole(string persona = "agent", int score = 7) => new(
        persona, "A verdict.", "A review.", ["works"], ["does not"], "a condition")
    {
        Score = score,
        ScoreReason = "Why this number.",
    };
}
