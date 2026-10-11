using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

/// <summary>The sync line names a day in the viewer's zone, through the Domain's own day label, not the date of a UTC instant.</summary>
public class HouseholdSyncAgoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.CreateCustomTimeZone("test-pacific", TimeSpan.FromHours(-8), "test", "test");

    [Fact]
    public void AnInstantJustAfterMidnightUtcIsStillTheDayBeforeInTheViewersZone()
    {
        var at = new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero);

        var line = HouseholdSyncText.Line(HouseholdSyncStatus.Synced(at), Now, Pacific);

        Assert.Equal("Synced with Drive on Oct 2.", line);
    }

    [Fact]
    public void YesterdayAndTheWeekdayUseTheDomainsDayLabel()
    {
        Assert.Equal("Synced with Drive yesterday.", HouseholdSyncText.Line(HouseholdSyncStatus.Synced(Now.AddHours(-30)), Now, TimeZoneInfo.Utc));
        Assert.Equal("Synced with Drive on Friday.", HouseholdSyncText.Line(HouseholdSyncStatus.Synced(Now.AddDays(-3)), Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void ASyncStampedAheadOfThePhonesClockIsJustNow() =>
        Assert.Equal("Synced with Drive just now.", HouseholdSyncText.Line(HouseholdSyncStatus.Synced(Now.AddMinutes(5)), Now, TimeZoneInfo.Utc));
}
