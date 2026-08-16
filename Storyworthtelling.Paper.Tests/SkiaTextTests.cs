using SkiaSharp;
using Storyworthtelling.Paper;

namespace Storyworthtelling.Paper.Tests;

public class SkiaTextTests
{
    [Fact]
    public void WrapsAcrossMultipleLinesWhenTextExceedsWidth()
    {
        using var font = new SKFont(EmbeddedTypefaces.SansRegular, 20);
        var lines = SkiaText.Wrap(font, "the quick brown fox jumps over the lazy dog", 80f);

        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(font.MeasureText(l) <= 80f || !l.Contains(' ')));
    }

    [Fact]
    public void KeepsShortTextOnOneLine()
    {
        using var font = new SKFont(EmbeddedTypefaces.SansRegular, 20);
        var lines = SkiaText.Wrap(font, "short", 500f);

        Assert.Single(lines);
        Assert.Equal("short", lines[0]);
    }

    [Fact]
    public void EmptyTextYieldsASingleEmptyLine()
    {
        using var font = new SKFont(EmbeddedTypefaces.SansRegular, 20);
        var lines = SkiaText.Wrap(font, "", 500f);

        Assert.Single(lines);
        Assert.Equal("", lines[0]);
    }
}
