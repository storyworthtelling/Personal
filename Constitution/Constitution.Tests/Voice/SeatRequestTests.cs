using Constitution.Domain.Voice;

namespace Constitution.Tests.Voice;

/// <summary>
/// What a seat is handed: its seat, the facts already reduced to words, and the plain sentence that
/// stands in when there is no voice. The fallback is required because a hidden seat is a bug (the
/// line is never hidden), so a request cannot be built without one.
/// </summary>
public class SeatRequestTests
{
    [Fact]
    public void CarriesTheSeatAndTheFactsInOrder()
    {
        var request = new SeatRequest(VoiceSeat.TodayTopLine, ["Strength at 5:30", "lentil soup tonight"]);

        Assert.Equal(VoiceSeat.TodayTopLine, request.Seat);
        Assert.Equal(["Strength at 5:30", "lentil soup tonight"], request.Facts);
    }

    [Fact]
    public void NoFactsIsRefusedBecauseThereIsNothingToSay()
    {
        var error = Assert.Throws<ArgumentException>(() => new SeatRequest(VoiceSeat.RestDayLine, []));
        Assert.Equal("facts", error.ParamName);
    }

    [Fact]
    public void ABlankFactIsRefused()
    {
        var error = Assert.Throws<ArgumentException>(() => new SeatRequest(VoiceSeat.RestDayLine, ["a rest day", "  "]));
        Assert.Equal("facts", error.ParamName);
    }

    [Fact]
    public void ASeatOutsideTheClosedListIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeatRequest((VoiceSeat)99, ["a rest day"]));
    }

    [Fact]
    public void FactsAreCopiedAndTidiedToOneLineEach()
    {
        var source = new List<string> { "  Sleep is down\nthis month  ", "the other   signals are close" };
        var request = new SeatRequest(VoiceSeat.BodyMonthly, source);
        source.Clear();

        Assert.Equal(["Sleep is down this month", "the other signals are close"], request.Facts);
    }
}
