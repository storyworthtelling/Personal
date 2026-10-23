using Constitution.Domain.Today;
using Constitution.Domain.Voice;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>C-4.1.2: today's workout as one clipping (book p.11). Invented facts throughout.</summary>
public class TodayModelTests
{
    private static TodayPlan Day(int? bpm) => new(new TimeOnly(5, 30), "strength", 40, DinnerPlan.At("lentil soup")) { ScoreBpm = bpm };

    [Fact]
    public void WorkoutLineHasTimeLengthTypeAndBand()
    {
        var clipping = TodayModel.Clipping(Day(132));

        Assert.NotNull(clipping);
        Assert.Equal("Workout · 5:30", clipping.Kicker);
        Assert.Equal("Strength, 40 minutes", clipping.Headline);
        Assert.Equal("Score 132 bpm · Steady", clipping.ScoreLine);
    }

    [Theory]
    [InlineData(118, "Score 118 bpm · Easy")]
    [InlineData(150, "Score 150 bpm · Steady")]
    [InlineData(151, "Score 151 bpm · Hard")]
    public void TheBandComesFromTheTempoBands(int bpm, string expected)
    {
        Assert.Equal(expected, TodayModel.Clipping(Day(bpm))!.ScoreLine);
    }

    [Fact]
    public void RestDayHasNoClipping()
    {
        Assert.Null(TodayModel.Clipping(new TodayPlan(null, null, null, DinnerPlan.Out)));
        Assert.Null(TodayModel.Clipping(null));
    }

    [Fact]
    public void NoSettingMeansNoScoreLine()
    {
        var clipping = TodayModel.Clipping(Day(null));

        Assert.NotNull(clipping);
        Assert.Null(clipping.ScoreLine);
    }

    [Fact]
    public void TheFolioIsSteadyForADayAndPlain()
    {
        var day = new DateOnly(2026, 11, 3);

        Assert.Equal(TodayModel.Folio(day), TodayModel.Folio(day));
        Assert.Matches(@"^— \d{2} —$", TodayModel.Folio(day));
    }
}
