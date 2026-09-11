using Foreword.Domain.Weather;
using Xunit;

namespace Foreword.Tests.Weather;

/// <summary>
/// The green window on D2 — the longest stretch of the day with no rain in it.
///
/// <para>It is on the page because it is the question the page is actually asked: not "will it
/// rain", which the bars already answer hour by hour, but "when can the lawn be cut". A run of dry
/// hours is the answer to that, and it is worth naming because reading it off seventeen bars is
/// exactly the work the paper is supposed to have done already.</para>
/// </summary>
public class DryWindowTests
{
    private static readonly DateOnly Day = new(2026, 9, 13);

    private static IReadOnlyList<HourWeather> Hours(params int[] percents) =>
        [.. percents.Select((percent, i) => new HourWeather(
            Day.ToDateTime(new TimeOnly(6 + i, 0)), 70, percent))];

    [Fact]
    public void AMorningOfRainThatStopsIsADryAfternoon()
    {
        // 6, 7, 8, 9 wet; 10 onwards dry.
        var window = DryWindow.Find(Hours(80, 70, 55, 40, 5, 0, 0, 0));

        Assert.NotNull(window);
        Assert.Equal(new TimeOnly(10, 0), TimeOnly.FromDateTime(window!.Value.Start));
        Assert.Equal(new TimeOnly(13, 0), TimeOnly.FromDateTime(window.Value.End));
    }

    /// <summary>
    /// The <b>longest</b> run, not the first. A ten-minute gap in the morning is not when you cut
    /// the lawn, and offering it as the window would be worse than offering nothing.
    /// </summary>
    [Fact]
    public void TheLongestDryRunWinsRatherThanTheEarliest()
    {
        var window = DryWindow.Find(Hours(0, 0, 90, 90, 0, 0, 0, 0, 90));

        Assert.NotNull(window);
        Assert.Equal(new TimeOnly(10, 0), TimeOnly.FromDateTime(window!.Value.Start));
        Assert.Equal(new TimeOnly(13, 0), TimeOnly.FromDateTime(window.Value.End));
    }

    /// <summary>
    /// A day of solid rain has no window, and says so by having none. A "driest hour" offered as a
    /// dry window would be a lie the page told with a straight face.
    /// </summary>
    [Fact]
    public void ADayOfRainHasNoWindow()
    {
        Assert.Null(DryWindow.Find(Hours(80, 70, 90, 60, 55, 70)));
    }

    /// <summary>
    /// A dry day is one window, the whole day — not no window. The distinction matters because the
    /// page draws the band, and a day with nothing to avoid should read as entirely clear rather
    /// than as a day nobody checked.
    /// </summary>
    [Fact]
    public void ADryDayIsOneWindowAcrossTheWholeDay()
    {
        var window = DryWindow.Find(Hours(0, 5, 10, 0, 0));

        Assert.NotNull(window);
        Assert.Equal(new TimeOnly(6, 0), TimeOnly.FromDateTime(window!.Value.Start));
        Assert.Equal(new TimeOnly(10, 0), TimeOnly.FromDateTime(window.Value.End));
    }

    /// <summary>
    /// One dry hour between two wet ones is not a window. Nothing worth doing outdoors fits in it,
    /// and drawing a band that narrow would put a label on the page with no room to read it.
    /// </summary>
    [Fact]
    public void ASingleDryHourIsNotAWindow()
    {
        Assert.Null(DryWindow.Find(Hours(80, 5, 80, 70, 90)));
    }

    [Fact]
    public void ADayWithNoHoursHasNoWindow()
    {
        Assert.Null(DryWindow.Find([]));
    }
}
