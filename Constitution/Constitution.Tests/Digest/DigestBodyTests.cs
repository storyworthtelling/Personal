using Constitution.Domain.Body;
using Constitution.Domain.Documents;

namespace Constitution.Tests.DigestPages;

/// <summary>C-2.6.2 — every signal, every week, against the owner's own last twelve weeks (D39).</summary>
public class DigestBodyTests
{
    [Fact]
    public void EverySignalEveryWeek()
    {
        var series = DigestBody.Series(DigestFixture.Flat(BodySignal.Sleep, 7.0), DigestFixture.Saturday);
        Assert.Equal(BodySignals.All.Count, series.Count);
        Assert.Equal(BodySignals.All.Order(), series.Select(item => item.Signal).Order());
        Assert.All(series, item => Assert.Equal(12, item.Weeks.Count));
    }

    [Fact]
    public void AMissingStretchIsAGapNotAZero()
    {
        var readings = DigestFixture.Flat(BodySignal.Weight, 180.0, days: 30);
        var series = DigestBody.Series(readings, DigestFixture.Saturday).Single(item => item.Signal == BodySignal.Weight);
        Assert.Null(series.Weeks[0]);
        Assert.NotNull(series.Weeks[11]);
        Assert.True(series.HasGap);
        var empty = DigestBody.Series(readings, DigestFixture.Saturday).Single(item => item.Signal == BodySignal.Sleep);
        Assert.All(empty.Weeks, week => Assert.Null(week));
        Assert.Null(empty.Latest);
    }

    [Fact]
    public void TwoReadingsAreNotAWeek()
    {
        var readings = new List<BodyReading>
        {
            new(BodySignal.Weight, DigestFixture.WeekStart.AddDays(1), 180.0),
            new(BodySignal.Weight, DigestFixture.WeekStart.AddDays(2), 180.0),
        };
        var series = DigestBody.Series(readings, DigestFixture.Saturday).Single(item => item.Signal == BodySignal.Weight);
        Assert.Null(series.Latest);
    }

    [Fact]
    public void TheLatestWeekIsADotWithItsValueAndTheChangeInWords()
    {
        var readings = new List<BodyReading>();
        for (var back = 1; back <= 84; back++)
        {
            var day = DigestFixture.Saturday.AddDays(-back);
            readings.Add(new BodyReading(BodySignal.Weight, day, day >= DigestFixture.WeekStart ? 178.0 : 180.0));
        }

        var series = DigestBody.Series(readings, DigestFixture.Saturday).Single(item => item.Signal == BodySignal.Weight);
        Assert.Equal(178.0, series.Latest!.Value, 3);
        Assert.Equal("178 lb", series.LatestText);
        Assert.Equal("down 2 lb", series.ChangeWords);
    }

    [Fact]
    public void ComparesOnlyToTheOwnersPast()
    {
        // The only inputs are one person's readings and a day; there is no second person to compare with.
        var parameters = typeof(DigestBody).GetMethod(nameof(DigestBody.Series))!.GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Equal([typeof(IEnumerable<BodyReading>), typeof(DateOnly)], parameters);

        var steady = DigestBody.Series(DigestFixture.Flat(BodySignal.Weight, 180.0), DigestFixture.Saturday)
            .Single(item => item.Signal == BodySignal.Weight);
        Assert.Equal("about the same", steady.ChangeWords);
    }

    [Fact]
    public void TooLittleHistoryGivesNoChangeWords()
    {
        var series = DigestBody.Series(DigestFixture.Flat(BodySignal.Weight, 180.0, days: 6), DigestFixture.Saturday)
            .Single(item => item.Signal == BodySignal.Weight);
        Assert.NotNull(series.Latest);
        Assert.Null(series.ChangeWords);
    }

    [Fact]
    public void TodaysUnfinishedDayTotalIsLeftOut()
    {
        var readings = DigestFixture.Flat(BodySignal.Steps, 7000, days: 30);
        readings.Add(new BodyReading(BodySignal.Steps, DigestFixture.Saturday, 40));
        var series = DigestBody.Series(readings, DigestFixture.Saturday).Single(item => item.Signal == BodySignal.Steps);
        Assert.Equal(7000, series.Latest!.Value, 3);
    }

    [Fact]
    public void TwoPagesCarryAllTheSignals()
    {
        var series = DigestBody.Series(DigestFixture.Flat(BodySignal.Sleep, 7.0), DigestFixture.Saturday);
        var pages = DigestBody.Pages(series, [], DigestFixture.Saturday, firstPage: 2, pages: 5);
        Assert.Equal(2, pages.Count);
        foreach (var signal in BodySignals.All)
        {
            var name = BodySignals.Name(signal);
            Assert.Single(pages.SelectMany(page => page.Printed), item => item.Name == $"signal-{BodySignals.Id(signal)}");
            Assert.NotEmpty(name);
        }
    }

    [Fact]
    public void TheChartsAreNeutralInk()
    {
        var series = DigestBody.Series(DigestFixture.Flat(BodySignal.Sleep, 7.0), DigestFixture.Saturday);
        var pages = DigestBody.Pages(series, [], DigestFixture.Saturday, 2, 5);
        Assert.All(pages.SelectMany(page => page.Printed), item => Assert.NotEqual(PrintedRole.Voice, item.Role));
    }
}
