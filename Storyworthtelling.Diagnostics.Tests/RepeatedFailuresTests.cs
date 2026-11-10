using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>
/// Audit 08 §33: a missing OMDb key wrote a multi-KB warning for every title in every refresh, rolling the log and
/// filling the outbox until segments were dropped. The first of a kind is logged in full; the rest are counted and said
/// once, when the next window opens.
/// </summary>
public class RepeatedFailuresTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheFirstIsLoggedAndTheRestCounted()
    {
        var gate = new RepeatedFailures(TimeSpan.FromMinutes(15));

        Assert.Equal((true, (string?)null), gate.Note("rating:NoKey", T0));
        Assert.Equal((false, (string?)null), gate.Note("rating:NoKey", T0.AddMinutes(1)));
        Assert.Equal((false, (string?)null), gate.Note("rating:NoKey", T0.AddMinutes(2)));

        // A new window: this one is logged again, with the count of the last.
        Assert.Equal((true, "2 more of the same in the 15 minutes before"), gate.Note("rating:NoKey", T0.AddMinutes(16)));
    }

    [Fact]
    public void KindsAreCountedApart()
    {
        var gate = new RepeatedFailures(TimeSpan.FromMinutes(15));

        Assert.True(gate.Note("rating:NoKey", T0).LogInFull);
        Assert.True(gate.Note("air-dates:Quota", T0).LogInFull);
    }
}
