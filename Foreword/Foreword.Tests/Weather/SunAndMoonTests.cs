using Foreword.Domain.Weather;
using Xunit;

namespace Foreword.Tests.Weather;

/// <summary>
/// The foot of the weather page: "Sunrise 6:32am · Sunset 7:21pm · 12h 49m of daylight · Waning
/// gibbous". Daylight is there because smart tasks can require it.
/// </summary>
public class SunAndMoonTests
{
    [Fact]
    public void DaylightIsTheDistanceBetweenThem()
    {
        var sun = new SunAndMoon(new TimeOnly(6, 32), new TimeOnly(19, 21), "WANING_GIBBOUS");

        Assert.Equal("12h 49m of daylight", SunLine.Daylight(sun));
    }

    [Fact]
    public void WithoutBothTimesThereIsNoDaylightToState()
    {
        Assert.Equal(string.Empty, SunLine.Daylight(new SunAndMoon(new TimeOnly(6, 32), null, "")));
        Assert.Equal(string.Empty, SunLine.Daylight(new SunAndMoon(null, null, "")));
    }

    /// <summary>
    /// The API says WANING_GIBBOUS and the page says "Waning gibbous". Left alone it would print a
    /// shouted enum, which is the sort of thing that survives to a printed page precisely because it
    /// is legible enough that nobody calls it a bug.
    /// </summary>
    [Fact]
    public void TheMoonPhaseIsWrittenAsWords()
    {
        Assert.Equal("Waning gibbous", SunLine.Moon("WANING_GIBBOUS"));
        Assert.Equal("Full moon", SunLine.Moon("FULL_MOON"));
        Assert.Equal("New moon", SunLine.Moon("NEW_MOON"));
    }

    [Fact]
    public void AnUnknownPhaseIsLeftOutRatherThanGuessed()
    {
        Assert.Equal(string.Empty, SunLine.Moon(""));
        Assert.Equal(string.Empty, SunLine.Moon("MOON_PHASE_UNSPECIFIED"));
    }
}
