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

    // ---- Dash's screens (B4; mock: People waiting, A conversation) ----

    [Fact]
    public void TheCardIsADarkGreyUnderTheWords()
    {
        // The mockup's rows and their bubbles sit on CARD, hsl(210 10% 11%).
        Assert.InRange(WatchTokens.Neutral.Card.C, 0, 0.02);
        Assert.True(WatchTokens.Neutral.Card.L > WatchTokens.Ground.L && WatchTokens.Neutral.Card.L < WatchTokens.Neutral.Faint.L);
        Assert.True(WatchContrast.Ratio(WatchTokens.Neutral.Ink, WatchTokens.Neutral.Card) >= 4.5);
        Assert.True(WatchContrast.Ratio(WatchTokens.Neutral.Soft, WatchTokens.Neutral.Card) >= 4.5);
    }

    [Fact]
    public void TheOwnersBubbleIsQuieterGlass() => Assert.Equal(0.35, WatchTokens.Light.Mine, 9);

    [Fact]
    public void TheRatioIsWcags()
    {
        Assert.Equal(21, WatchContrast.Ratio(new Oklch(1, 0, 0), WatchTokens.Ground), 1);
        Assert.Equal(1, WatchContrast.Ratio(WatchTokens.Neutral.Soft, WatchTokens.Neutral.Soft), 9);
        Assert.Equal(WatchContrast.Ratio(WatchTokens.Neutral.Ink, WatchTokens.Ground), WatchContrast.Ratio(WatchTokens.Ground, WatchTokens.Neutral.Ink), 9);
    }

    [Fact]
    public void APillsWordsAreTheBetterInk()
    {
        // The mockup labels a pill in near-black, which reads on a light glass and not on Dash's navy or maroon; the
        // label takes whichever of the near-black and the ink reads better (WD §12: words keep 4.5:1).
        var light = new Oklch(0.80, 0.16, 75);
        var dark = new Oklch(0.40, 0.12, 15);

        Assert.Equal(WatchTokens.Neutral.OnGlass, WatchContrast.WordsOn(light));
        Assert.Equal(WatchTokens.Neutral.Ink, WatchContrast.WordsOn(dark));
    }

    [Fact]
    public void DashsPillWordsReadAllDay()
    {
        for (var minute = 0; minute < 24 * 60; minute += 15)
        {
            var glass = DayColour.At(WatchApp.Dash, new TimeOnly(minute / 60, minute % 60));
            glass = glass with { L = Math.Clamp(glass.L + WatchTokens.GlyphLift(WatchApp.Dash), 0, 1) };

            Assert.True(WatchContrast.Ratio(WatchContrast.WordsOn(glass), glass) >= 4.5, $"At minute {minute} a pill's words don't read.");
        }
    }

    [Fact]
    public void TheNearBlackIsTheMockups() => Assert.InRange(WatchTokens.Neutral.OnGlass.L, 0.1, 0.2);

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

    [Fact]
    public void TheTilesEarlierNamesFadeAsDrawn()
    {
        // mock: Tile: People waiting draws the second name in the grey at 0.7 and the third at 0.4 (B8).
        Assert.Equal(0.7, WatchTokens.Light.Earlier);
        Assert.Equal(0.4, WatchTokens.Light.Earliest);
    }
}
