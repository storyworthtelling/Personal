using Constitution.Domain.Time;
using Constitution.Domain.Today;
using Constitution.Domain.Voice;

namespace Constitution.Tests.Today;

/// <summary>
/// C-4.1.1: the line at the top of Today (book p.11, p.18). Invented facts throughout. At Release A
/// no plan exists, so the neutral sentence stands in; the same model takes a plan later.
/// </summary>
public class TodaySeatTests
{
    private static TodayPlan WorkoutDay() =>
        new(new TimeOnly(5, 30), "strength", 40, DinnerPlan.At("lentil soup"));

    private static TodayPlan RestDayDinnerOut() => new(null, null, null, DinnerPlan.Out);

    [Fact]
    public void FallsBackNeverHides()
    {
        var line = TodayModel.TopLine(WorkoutDay());

        Assert.Equal("Strength at 5:30. Lentil soup tonight.", line.Text);
        Assert.Equal(VoiceSeat.TodayTopLine, line.Seat);
        Assert.False(line.IsVoiced);
    }

    [Fact]
    public void RestDayUsesItsOwnSeat()
    {
        var line = TodayModel.TopLine(RestDayDinnerOut());

        Assert.Equal("A rest day. Dinner is out tonight.", line.Text);
        Assert.Equal(VoiceSeat.RestDayLine, line.Seat);
        Assert.False(line.IsVoiced);
    }

    [Fact]
    public void ThePlansRequestCarriesTheSameFallbackItShows()
    {
        var line = TodayModel.TopLine(WorkoutDay());

        Assert.NotNull(line.Request);
        Assert.Equal(line.Text, line.Request!.Fallback);
        Assert.Equal(line.Seat, line.Request.Seat);
    }

    [Fact]
    public void NoPlanIsTheNeutralSentenceNeverEmpty()
    {
        var line = TodayModel.TopLine(null);

        Assert.Equal("Nothing is planned yet.", line.Text);
        Assert.Equal(VoiceSeat.TodayTopLine, line.Seat);
        Assert.False(line.IsVoiced);
        Assert.Null(line.Request);
    }

    [Fact]
    public void AWorkoutWithNoDinnerYetIsStillNeutralNotEmpty()
    {
        var line = TodayModel.TopLine(new TodayPlan(new TimeOnly(5, 30), "strength", 40, null));

        Assert.Equal("Nothing is planned yet.", line.Text);
        Assert.Null(line.Request);
    }

    [Fact]
    public void ARestDayWithNoDinnerYetIsStillNeutral()
    {
        Assert.Equal("Nothing is planned yet.", TodayModel.TopLine(new TodayPlan(null, null, null, null)).Text);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public void AHalfWorkoutIsRefused(bool start, bool kind, bool minutes)
    {
        Assert.Throws<ArgumentException>(() => new TodayPlan(
            start ? new TimeOnly(5, 30) : null, kind ? "strength" : null, minutes ? 40 : null, DinnerPlan.Out));
    }

    [Fact]
    public void ANonPositiveWorkoutLengthIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TodayPlan(new TimeOnly(5, 30), "strength", 0, DinnerPlan.Out));
    }

    [Fact]
    public void TheHeaderReadsTheDateThroughTheSharedHelper()
    {
        var moment = new DateTime(2026, 11, 17, 9, 0, 0);

        Assert.Equal("Tuesday, Nov 17", TodayModel.Header(moment));
        Assert.Equal(TodayHeader.Date(moment), TodayModel.Header(moment));
    }

    [Fact]
    public void TheOnlyActionIsIAteSomethingElse_AndItIsInertUntilReleaseD()
    {
        Assert.Equal("I ate something else", TodayModel.ActionLabel);
        Assert.False(TodayModel.ActionIsLive);
    }
}
