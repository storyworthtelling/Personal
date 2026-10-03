using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Snowball;

/// <summary>The pure halves of the import gate: when the history is already here, and how long to leave
/// Drive alone after a read that did not work.</summary>
public class SnowballHistoryGateTests
{
    private static readonly DateOnly Last = new(2024, 9, 27);

    [Fact]
    public void NoSnapshotAtAllLeavesTheGateOpen() =>
        Assert.False(SnowballHistoryGate.IsAlreadyPresent(null, Last));

    [Theory]
    [InlineData(-400)]
    [InlineData(-7)]
    [InlineData(0)] // on the last recorded week itself
    public void ASnapshotOnOrBeforeTheLastRecordedWeekMeansItIsPresent(int offsetDays) =>
        Assert.True(SnowballHistoryGate.IsAlreadyPresent(Last.AddDays(offsetDays), Last));

    [Theory]
    [InlineData(1)]
    [InlineData(7)] // the very next weekly row
    [InlineData(700)]
    public void ASnapshotAfterTheLastRecordedWeekNeverCloseTheGate(int offsetDays) =>
        Assert.False(SnowballHistoryGate.IsAlreadyPresent(Last.AddDays(offsetDays), Last));

    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ABrandNewBackoffAllowsTheFirstAttempt() =>
        Assert.True(new SnowballHistoryBackoff().MayAttempt(T0, "k"));

    [Fact]
    public void AttemptsAreRefusedUntilThePauseHasPassed()
    {
        var backoff = new SnowballHistoryBackoff(TimeSpan.FromMinutes(5));
        backoff.RecordAttempt(T0, "k");

        Assert.False(backoff.MayAttempt(T0.AddSeconds(1), "k"));
        Assert.False(backoff.MayAttempt(T0.AddMinutes(4), "k"));
        Assert.True(backoff.MayAttempt(T0.AddMinutes(5), "k"));
    }

    [Fact]
    public void ADifferentKeyIsNotHeldBackByAnotherKeysAttempt()
    {
        var backoff = new SnowballHistoryBackoff(TimeSpan.FromMinutes(5));
        backoff.RecordAttempt(T0, "first");

        Assert.True(backoff.MayAttempt(T0.AddSeconds(1), "second"));
    }

    [Fact]
    public void ANullKeyIsAKeyLikeAnyOther()
    {
        var backoff = new SnowballHistoryBackoff(TimeSpan.FromMinutes(5));
        backoff.RecordAttempt(T0, null);

        Assert.False(backoff.MayAttempt(T0.AddSeconds(1), null));
    }

    [Fact]
    public void TheBackoffNeverStoresTheKeyItselfOnlyAHashOfIt()
    {
        var backoff = new SnowballHistoryBackoff(TimeSpan.FromMinutes(5));
        backoff.RecordAttempt(T0, "a-secret-passphrase");

        var fields = typeof(SnowballHistoryBackoff).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            var held = field.GetValue(backoff);
            Assert.DoesNotContain("a-secret-passphrase", held?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
