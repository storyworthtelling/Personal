using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>Audit 08 §25: one ISO-8601 stamp, to the millisecond and with the phone's offset, for every file line.</summary>
public class LogLineTests
{
    [Fact]
    public void TheStampIsIsoWithMillisecondsAndOffset() =>
        Assert.Equal("2026-10-30T14:30:05.123-05:00", LogLine.Stamp(new DateTimeOffset(2026, 10, 30, 14, 30, 5, 123, TimeSpan.FromHours(-5))));

    [Fact]
    public void ALineOpensWithTheStamp() =>
        Assert.Equal(
            "[2026-10-30T14:30:05.000+00:00] WARN Sync — failed\n",
            LogLine.Format(new DateTimeOffset(2026, 10, 30, 14, 30, 5, TimeSpan.Zero), "WARN", "Sync", "failed", null));
}
