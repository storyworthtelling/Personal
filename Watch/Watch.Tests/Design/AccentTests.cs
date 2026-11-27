using Watch.Domain.Design;

namespace Watch.Tests.Design;

/// <summary>
/// Words in an app's own colour (WD §4.3: no coloured text other than an app's accent) keep 4.5:1 on black (WD §12), so
/// an accent too dark to read at that hour is written in the ink instead (P14 B5: "Tap to undo"), as B4's pills do.
/// </summary>
public class AccentTests
{
    [Fact]
    public void AnAccentThatReadsIsKept()
    {
        var light = WatchTokens.Neutral.Ink;

        Assert.Equal(light, WatchContrast.Accent(light));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 0)]
    [InlineData(16, 20)]
    public void DashsDarkGlassIsWrittenInTheInk(int hour, int minute)
    {
        var dash = DayColour.At(WatchApp.Dash, new TimeOnly(hour, minute));

        Assert.True(WatchContrast.Ratio(dash, WatchTokens.Ground) < 4.5);
        Assert.Equal(WatchTokens.Neutral.Ink, WatchContrast.Accent(dash));
    }

    [Fact]
    public void TheAccentAlwaysReads()
    {
        foreach (var app in Enum.GetValues<WatchApp>())
        {
            for (var hour = 0; hour < 24; hour++)
            {
                var accent = WatchContrast.Accent(DayColour.At(app, new TimeOnly(hour, 0)));
                Assert.True(WatchContrast.Ratio(accent, WatchTokens.Ground) >= 4.5, $"{app} at {hour}:00");
            }
        }
    }
}
