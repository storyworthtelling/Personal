using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class NightlyRescanScheduleTests
{
    [Fact]
    public void BeforeTargetHour_DelaysUntilLaterToday()
    {
        var now = new DateTimeOffset(2026, 9, 19, 1, 0, 0, TimeSpan.Zero);

        var delay = NightlyRescanSchedule.DelayUntilNext(now, hour: 3, minute: 0);

        Assert.Equal(TimeSpan.FromHours(2), delay);
    }

    [Fact]
    public void AfterTargetHour_DelaysUntilTomorrow()
    {
        var now = new DateTimeOffset(2026, 9, 19, 5, 0, 0, TimeSpan.Zero);

        var delay = NightlyRescanSchedule.DelayUntilNext(now, hour: 3, minute: 0);

        Assert.Equal(TimeSpan.FromHours(22), delay);
    }

    [Fact]
    public void ExactlyAtTargetHour_DelaysAFullDay()
    {
        // Matches DeliverySchedule's own <= check: hitting the target exactly counts as
        // "already happened" so a worker firing right on the minute rolls to tomorrow
        // rather than firing again immediately.
        var now = new DateTimeOffset(2026, 9, 19, 3, 0, 0, TimeSpan.Zero);

        var delay = NightlyRescanSchedule.DelayUntilNext(now, hour: 3, minute: 0);

        Assert.Equal(TimeSpan.FromHours(24), delay);
    }

    [Fact]
    public void UsesTheOffsetItIsGiven()
    {
        // Mirrors DeliverySchedule.DelayUntilNext's own load-bearing case: the target is
        // built in the SAME offset as `now`, so a local clock stays local rather than
        // silently aiming at UTC.
        var now = new DateTimeOffset(2026, 9, 19, 1, 0, 0, TimeSpan.FromHours(-5));

        var delay = NightlyRescanSchedule.DelayUntilNext(now, hour: 3, minute: 0);

        Assert.Equal(TimeSpan.FromHours(2), delay);
    }

    [Fact]
    public void DefaultHourAndMinute_MatchTheConstants()
    {
        var now = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);

        var delay = NightlyRescanSchedule.DelayUntilNext(now);

        Assert.Equal(
            NightlyRescanSchedule.DelayUntilNext(now, NightlyRescanSchedule.ScanHour, NightlyRescanSchedule.ScanMinute),
            delay);
    }
}
