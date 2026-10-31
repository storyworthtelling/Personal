using Constitution.Domain.Documents;

namespace Constitution.Tests.DigestPages;

/// <summary>Saturday scheduling — the digest is due from four on Saturday morning, with a Sunday catch-up, and on no other day.</summary>
public class DigestScheduleTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-5), "test", "test");

    private static DateTimeOffset Local(int day, int hour, int minute = 0) => new(2026, 3, day, hour, minute, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void NotDueBeforeSaturdayMorning()
    {
        Assert.Null(DigestSchedule.DueSaturday(Local(13, 12), Zone));
        Assert.Null(DigestSchedule.DueSaturday(Local(14, 3, 59), Zone));
    }

    [Fact]
    public void DueFromFourOnSaturday()
    {
        Assert.Equal(new DateOnly(2026, 3, 14), DigestSchedule.DueSaturday(Local(14, 4), Zone));
        Assert.Equal(new DateOnly(2026, 3, 14), DigestSchedule.DueSaturday(Local(14, 23, 59), Zone));
    }

    [Fact]
    public void SundayCatchesUpTheSaturdayBefore()
    {
        Assert.Equal(new DateOnly(2026, 3, 14), DigestSchedule.DueSaturday(Local(15, 9), Zone));
        Assert.Null(DigestSchedule.DueSaturday(Local(16, 9), Zone));
    }

    [Fact]
    public void TheHouseholdsClockDecidesNotUtc()
    {
        // 03:00 UTC on Saturday is 22:00 Friday here.
        Assert.Null(DigestSchedule.DueSaturday(new DateTimeOffset(2026, 3, 14, 3, 0, 0, TimeSpan.Zero), Zone));
        // 12:00 UTC on Saturday is 07:00 Saturday here.
        Assert.Equal(new DateOnly(2026, 3, 14), DigestSchedule.DueSaturday(new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero), Zone));
    }

    [Fact]
    public void TheWorkNameAndWorkerAreFrozenLiterals()
    {
        Assert.Equal("constitution-saturday-digest", Constitution.Domain.ConstitutionIdentity.DigestWorkName);
        Assert.Equal(Constitution.Domain.ConstitutionIdentity.ApplicationId + ".DigestWorker", Constitution.Domain.ConstitutionIdentity.DigestWorkerName);
        Assert.Contains("constitution-saturday-digest", Constitution.Domain.ConstitutionIdentity.WorkNames);
        Assert.Contains(Constitution.Domain.ConstitutionIdentity.DigestWorkerName, Constitution.Domain.ConstitutionIdentity.Workers);
    }
}
