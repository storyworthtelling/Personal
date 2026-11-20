using Watch.Domain.Design;

namespace Watch.Tests.Design;

public class DayColourTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void MidnightIsTheNightEnd()
    {
        foreach (var app in Enum.GetValues<WatchApp>())
        {
            AssertClose(WatchTokens.RangeOf(app).Night, DayColour.At(app, new TimeOnly(0, 0)));
        }
    }

    [Fact]
    public void NoonIsTheDayEnd()
    {
        foreach (var app in Enum.GetValues<WatchApp>())
        {
            AssertClose(WatchTokens.RangeOf(app).Day, DayColour.At(app, new TimeOnly(12, 0)));
        }
    }

    [Fact]
    public void NeverJumps()
    {
        foreach (var app in Enum.GetValues<WatchApp>())
        {
            var previous = DayColour.At(app, new TimeOnly(0, 0));
            for (var minute = 1; minute <= 24 * 60; minute++)
            {
                var current = DayColour.At(app, new TimeOnly(0, 0).AddMinutes(minute));
                Assert.True(Math.Abs(current.L - previous.L) < 0.002, $"{app} L at minute {minute}");
                Assert.True(Math.Abs(current.C - previous.C) < 0.002, $"{app} C at minute {minute}");
                Assert.True(HueDistance(current.H, previous.H) < 0.5, $"{app} hue at minute {minute}");
                previous = current;
            }
        }
    }

    [Fact]
    public void ShorterWayRound()
    {
        var hue = DayColour.At(WatchApp.Dash, new TimeOnly(6, 0)).H;
        var unwrapped = hue < 262 ? hue + 360 : hue;

        Assert.InRange(unwrapped, 262, 375);
        Assert.True(HueDistance(hue, 140) > 90, $"Dash at six passed near green: {hue}");
    }

    [Fact]
    public void HueStaysOnTheWheel()
    {
        foreach (var app in Enum.GetValues<WatchApp>())
        {
            for (var hour = 0; hour < 24; hour++)
            {
                var hue = DayColour.At(app, new TimeOnly(hour, 0)).H;
                Assert.InRange(hue, 0, 360 - Tolerance);
            }
        }
    }

    [Fact]
    public void DarkEndsStayDark()
    {
        Assert.Equal(0.30, DayColour.At(WatchApp.Dash, new TimeOnly(0, 0)).L, 9);
    }

    [Fact]
    public void BeatRunsGreenToRed()
    {
        Assert.Equal(150, DayColour.At(WatchApp.Beat, new TimeOnly(0, 0)).H, 9);
        Assert.Equal(27, DayColour.At(WatchApp.Beat, new TimeOnly(12, 0)).H, 9);
    }

    [Fact]
    public void MorningAndEveningMatch()
    {
        AssertClose(DayColour.At(WatchApp.Wave, new TimeOnly(9, 0)), DayColour.At(WatchApp.Wave, new TimeOnly(15, 0)));
    }

    private static void AssertClose(Oklch expected, Oklch actual)
    {
        Assert.Equal(expected.L, actual.L, 9);
        Assert.Equal(expected.C, actual.C, 9);
        Assert.True(HueDistance(expected.H, actual.H) < 1e-6, $"hue {actual.H}, expected {expected.H}");
    }

    private static double HueDistance(double first, double second)
    {
        var difference = Math.Abs(first - second) % 360;
        return difference > 180 ? 360 - difference : difference;
    }
}
