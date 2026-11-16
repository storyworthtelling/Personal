using Codex.Domain.Voice;
using Xunit;

namespace Codex.Tests.Voice;

/// <summary>
/// The part of the day a greeting is told, so a line written in the morning is not shown in the evening: it is
/// one of the greeting's facts, and the suite's one rule (<c>SeatPlan</c>, audit W10.3) writes it again when it moves.
/// </summary>
public class PartOfDayTests
{
    [Theory]
    [InlineData(5, "morning")]
    [InlineData(11, "morning")]
    [InlineData(12, "afternoon")]
    [InlineData(16, "afternoon")]
    [InlineData(17, "evening")]
    [InlineData(2, "evening")]
    public void ThePartOfTheDayFollowsTheClock(int hour, string expected) =>
        Assert.Equal(expected, PartOfDay.Of(new DateTime(2026, 11, 14, hour, 0, 0)));
}
