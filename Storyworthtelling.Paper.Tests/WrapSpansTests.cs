using SkiaSharp;
using Storyworthtelling.Paper;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// Wrapping that keeps its offsets, which is what a proofreading mark lands on. A span that is off
/// by a character puts the mark on the wrong word, and the page still looks like a page.
/// </summary>
public class WrapSpansTests
{
    private static readonly SKFont Font = new(SKTypeface.Default, 12);

    [Fact]
    public void EverySpanQuotesTheTextItCameFrom()
    {
        const string text = "The knight rode to the castle and knocked upon the gate at dusk.";

        var lines = SkiaText.WrapSpans(Font, text, 80);

        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var quoted = text[line.Start..line.End];
            Assert.Equal(quoted.Trim(), quoted);
            Assert.NotEmpty(quoted);
        }
    }

    [Fact]
    public void TheLinesRunForwardsAndCoverEveryWord()
    {
        const string text = "The knight rode to the castle and knocked upon the gate at dusk.";

        var lines = SkiaText.WrapSpans(Font, text, 80);

        for (var i = 1; i < lines.Count; i++)
            Assert.True(lines[i].Start >= lines[i - 1].End);

        var rebuilt = string.Join(' ', lines.Select(line => text[line.Start..line.End]));
        Assert.Equal(text, rebuilt);
    }

    /// <summary>A line the author ended is not a line the wrap may extend.</summary>
    [Fact]
    public void ANewlineIsAHardBreak()
    {
        const string text = "Short.\nAlso short.";

        var lines = SkiaText.WrapSpans(Font, text, 400);

        Assert.Equal(2, lines.Count);
        Assert.Equal("Short.", text[lines[0].Start..lines[0].End]);
        Assert.Equal("Also short.", text[lines[1].Start..lines[1].End]);
    }

    /// <summary>A blank line between paragraphs is kept, because it is the paragraph break.</summary>
    [Fact]
    public void ABlankLineSurvives()
    {
        const string text = "One.\n\nTwo.";

        var lines = SkiaText.WrapSpans(Font, text, 400);

        Assert.Equal(3, lines.Count);
        Assert.Equal(0, lines[1].Length);
    }

    [Fact]
    public void AWordWiderThanTheColumnStillGetsALine()
    {
        const string text = "Antidisestablishmentarianism";

        var lines = SkiaText.WrapSpans(Font, text, 10);

        var line = Assert.Single(lines);
        Assert.Equal(text, text[line.Start..line.End]);
    }

    [Fact]
    public void EmptyTextWrapsToNothing()
    {
        Assert.Empty(SkiaText.WrapSpans(Font, string.Empty, 100));
    }
}
