using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

// libgm reports message times in microseconds; the bridge passes them on as "timestampMs".
// Read as milliseconds, a real time lands tens of thousands of years out and throws.
public class BridgeTimestampTests
{
    private static readonly DateTimeOffset When = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Microseconds_AreReadAsMicroseconds()
    {
        Assert.Equal(When, BridgeTimestamp.ToInstant(When.ToUnixTimeMilliseconds() * 1000));
    }

    [Fact]
    public void Milliseconds_AreStillRead()
    {
        Assert.Equal(When, BridgeTimestamp.ToInstant(When.ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void Nothing_IsTheEpoch()
    {
        Assert.Equal(DateTimeOffset.UnixEpoch, BridgeTimestamp.ToInstant(0));
    }
}
