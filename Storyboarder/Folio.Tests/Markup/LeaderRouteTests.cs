using Folio.Domain.Markup;

namespace Folio.Tests.Markup;

/// <summary>
/// A note's leader runs to the phrase it is about, not to the line (R6). With two or three notes on one
/// line, a leader that stops at the line's right edge leaves the reader guessing which phrase each note
/// means; one that ends under the phrase's first word does not.
/// </summary>
public class LeaderRouteTests
{
    private const double Baseline = 200;
    private const double PhraseLeft = 140;
    private const double PhraseRight = 190;
    private const double Gutter = 340;
    private const double NoteLeft = 360;
    private const double NoteY = 260;

    [Fact]
    public void TheLeaderEndsInsideTheAnchoredPhrasesBox()
    {
        var end = LeaderRoute.For(PhraseLeft, PhraseRight, Baseline, Gutter, NoteLeft, NoteY, stack: 0)[^1];

        Assert.InRange(end.X, PhraseLeft, PhraseRight);
        Assert.InRange(end.Y, Baseline - MarkupGeometry.TypeSize, Baseline + LeaderRoute.Drop);
    }

    [Fact]
    public void ItStartsAtTheNoteAndEveryStretchIsLevelOrUpright()
    {
        var route = LeaderRoute.For(PhraseLeft, PhraseRight, Baseline, Gutter, NoteLeft, NoteY, stack: 0);

        Assert.Equal(new LeaderPoint(NoteLeft, NoteY), route[0]);
        for (var i = 1; i < route.Count; i++)
            Assert.True(route[i].X == route[i - 1].X || route[i].Y == route[i - 1].Y,
                $"Stretch {i} runs on a slant; a diagonal across prose reads as a mark on the prose.");
    }

    /// <summary>
    /// The run back along the line sits in the white between this line and the next: under a query's
    /// underline, so it does not read as a second one, and clear of the next line's capitals.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void TheRunUnderTheLineStaysInTheGapBetweenLines(int stack)
    {
        var route = LeaderRoute.For(PhraseLeft, PhraseRight, Baseline, Gutter, NoteLeft, NoteY, stack);
        var level = route.Where((point, i) => i > 0 && point.Y == route[i - 1].Y && point.X < Gutter).Select(point => point.Y).Single();

        Assert.True(level > Baseline + 2, "The run should sit below a query’s underline.");
        var nextCaps = Baseline + MarkupGeometry.LineHeight - (0.7 * MarkupGeometry.TypeSize);
        Assert.True(level < nextCaps, "The run should stay clear of the next line’s capitals.");
    }

    [Fact]
    public void TwoNotesOnOneLineRunAtDifferentLevels()
    {
        var first = LeaderRoute.For(PhraseLeft, PhraseRight, Baseline, Gutter, NoteLeft, NoteY, stack: 0);
        var second = LeaderRoute.For(PhraseLeft, PhraseRight, Baseline, Gutter, NoteLeft, NoteY, stack: 1);

        Assert.NotEqual(first[^2].Y, second[^2].Y);
    }

    /// <summary>A caret is a point; its leader ends at that point.</summary>
    [Fact]
    public void ACaretsLeaderEndsAtTheCaret()
    {
        var end = LeaderRoute.For(PhraseLeft, PhraseLeft, Baseline, Gutter, NoteLeft, NoteY, stack: 0)[^1];

        Assert.Equal(PhraseLeft, end.X);
    }
}
