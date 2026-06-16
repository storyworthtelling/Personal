using FocusPhone.Domain.Unlock;

namespace FocusPhone.Tests.Unlock;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
