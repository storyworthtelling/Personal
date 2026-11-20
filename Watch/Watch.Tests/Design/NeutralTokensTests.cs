using Watch.Domain.Design;

namespace Watch.Tests.Design;

/// <summary>WD §4.3 and §12: the words are cool greys that keep 4.5:1 on black; Dash's glass is lifted (mock).</summary>
public class NeutralTokensTests
{
    [Fact]
    public void InkReadsOnBlack() => Assert.True(Contrast(WatchTokens.Neutral.Ink) >= 4.5);

    [Fact]
    public void SoftReadsOnBlack() => Assert.True(Contrast(WatchTokens.Neutral.Soft) >= 4.5);

    [Fact]
    public void InkOverSoftOverFaint()
    {
        Assert.True(WatchTokens.Neutral.Ink.L > WatchTokens.Neutral.Soft.L);
        Assert.True(WatchTokens.Neutral.Soft.L > WatchTokens.Neutral.Faint.L);
        Assert.True(WatchTokens.Neutral.Faint.L > 0);
    }

    [Fact]
    public void NeutralsHaveNoColour()
    {
        Assert.All(
            [WatchTokens.Neutral.Ink, WatchTokens.Neutral.Soft, WatchTokens.Neutral.Faint],
            neutral => Assert.InRange(neutral.C, 0, 0.02));
    }

    [Fact]
    public void OnlyDashIsLifted()
    {
        Assert.Equal(0.08, WatchTokens.GlyphLift(WatchApp.Dash), 9);
        Assert.Equal(0, WatchTokens.GlyphLift(WatchApp.Wave), 9);
    }

    [Fact]
    public void UnplayedIsFainter() => Assert.InRange(WatchTokens.Light.Unplayed, 0.1, 0.4);

    private static double Contrast(Oklch colour)
    {
        var argb = Srgb.Argb(colour);
        var luminance = 0.2126 * Linear((argb >> 16) & 0xFF) + 0.7152 * Linear((argb >> 8) & 0xFF) + 0.0722 * Linear(argb & 0xFF);
        return (luminance + 0.05) / 0.05;
    }

    private static double Linear(int channel)
    {
        var unit = channel / 255.0;
        return unit <= 0.04045 ? unit / 12.92 : Math.Pow((unit + 0.055) / 1.055, 2.4);
    }
}
