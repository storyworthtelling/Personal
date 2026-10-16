namespace Codex.Domain.Post;

/// <summary>
/// Reads a message time from the Messages-for-Web bridge. libgm carries them in
/// microseconds, and the bridge passes the number on under the name "timestampMs"; read as
/// milliseconds it is a date tens of thousands of years out, which DateTimeOffset refuses.
/// Anything past the millisecond range a real date could reach is taken as microseconds.
/// </summary>
public static class BridgeTimestamp
{
    // Milliseconds since 1970 stay under this until the year 5138.
    private const long LargestMilliseconds = 100_000_000_000_000;

    public static DateTimeOffset ToInstant(long value) =>
        value > LargestMilliseconds
            ? DateTimeOffset.UnixEpoch.AddTicks(value * (TimeSpan.TicksPerMillisecond / 1000))
            : DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, value));
}
