using Folio.Domain.Time;

namespace Folio.Tests.Support;

// Deterministic clock for testing time-dependent logic without real waits.
public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}
