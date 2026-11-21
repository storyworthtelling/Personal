using Watch.Domain.Design;
using Watch.Domain.Face;

namespace Watch.Tests.Face;

/// <summary>WD §4.1, §10: the minute gives the hue; each tone keeps one lightness and chroma all hour.</summary>
public class FaceColourTests
{
    private static readonly Func<double, Oklch>[] Tones =
    [
        FaceColour.Glass, FaceColour.Rim, FaceColour.Ring, FaceColour.Date, FaceColour.AlwaysOn, FaceColour.AlwaysOnRing,
    ];

    [Fact]
    public void HueIsTheMinute()
    {
        var hue = HourShape.For(new TimeOnly(10, 15)).Hue;

        Assert.All(Tones, tone => Assert.Equal(90 + WatchTokens.Face.RedHue, tone(hue).H, 6));
    }

    [Fact]
    public void TopOfTheHourIsRed()
    {
        var red = FaceColour.Glass(HourShape.For(new TimeOnly(9, 0)).Hue);
        var argb = Srgb.Argb(red);

        Assert.Equal(WatchTokens.Face.RedHue, red.H, 6);
        Assert.True(((argb >> 16) & 0xFF) > 2 * ((argb >> 8) & 0xFF), "Red leads green at the top of the hour.");
        Assert.True(((argb >> 16) & 0xFF) > 2 * (argb & 0xFF), "Red leads blue at the top of the hour.");
    }

    [Fact]
    public void HueStaysOnTheWheel()
    {
        Assert.All(Tones, tone => Assert.InRange(tone(359).H, 0, 360));
    }

    [Fact]
    public void LightnessNeverMoves()
    {
        foreach (var tone in Tones)
        {
            var first = tone(0);
            Assert.True(first.L > 0, "A tone with no light.");
            for (var minute = 1; minute < 60; minute++)
            {
                var colour = tone(HourShape.For(new TimeOnly(0, minute)).Hue);
                Assert.Equal(first.L, colour.L, 9);
                Assert.Equal(first.C, colour.C, 9);
            }
        }
    }

    [Fact]
    public void AlwaysOnIsAThird()
    {
        Assert.Equal(FaceColour.Rim(0).L / 3, FaceColour.AlwaysOn(0).L, 6);
        Assert.Equal(FaceColour.Ring(0).L / 3, FaceColour.AlwaysOnRing(0).L, 6);
    }

    [Fact]
    public void DateIsQuiet()
    {
        Assert.True(FaceColour.Date(0).C < FaceColour.Glass(0).C / 3, "The date is the shape's colour at low saturation.");
        Assert.True(FaceColour.Date(0).L < FaceColour.Rim(0).L);
    }

    [Fact]
    public void RingIsQuieterThanTheRim()
    {
        Assert.InRange(WatchTokens.Face.RingAlpha, 0.5, 0.9);
        Assert.True(FaceColour.Ring(0).L <= FaceColour.Rim(0).L);
    }

    [Fact]
    public void GlassLightsHighAndShadesLow()
    {
        Assert.Equal(1, WatchTokens.Face.Highlight.L);
        Assert.Equal(0, WatchTokens.Face.Shade.L);
        Assert.InRange(WatchTokens.Face.HighlightAlpha, 0.2, 0.8);
        Assert.InRange(WatchTokens.Face.ShadeAlpha, 0.2, 0.8);
    }

    [Fact]
    public void GroundIsBlack()
    {
        Assert.Equal(unchecked((int)0xFF000000), Srgb.Argb(WatchTokens.Ground));
    }
}
