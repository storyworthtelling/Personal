using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

/// <summary>
/// Pulp called every failed household pass "offline" (audit 11 §20): a revoked Google sign-in read as "Drive couldn't be
/// reached", so nothing said to sign in again, and the exception's own message went into the status. Now offline is
/// only offline; a refused sign-in says so; anything else is a plain sentence. None of them is ever a raw error.
/// </summary>
public class HouseholdSyncFailureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARefusedSignInAsksForANewOneAndIsNotCalledOffline()
    {
        var status = HouseholdSyncStatus.Synced(Now.AddHours(-2)).SignInRefused();

        Assert.Equal(HouseholdSyncState.SignInNeeded, status.State);
        Assert.Equal(Now.AddHours(-2), status.LastSynced);
        var line = HouseholdSyncText.Line(status, Now);
        Assert.Contains("sign in again", line, StringComparison.Ordinal);
        Assert.DoesNotContain("couldn't be reached", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AnyOtherFailureIsAPlainRetryNotOffline()
    {
        var status = HouseholdSyncStatus.Synced(Now).PassFailed();

        Assert.Equal(HouseholdSyncState.Retrying, status.State);
        var line = HouseholdSyncText.Line(status, Now);
        Assert.Contains("tries again", line, StringComparison.Ordinal);
        Assert.DoesNotContain("couldn't be reached", line, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflineStillSaysDriveCouldNotBeReached()
    {
        var line = HouseholdSyncText.Line(HouseholdSyncStatus.Synced(Now).WentOffline("no network"), Now);

        Assert.Contains("couldn't be reached", line, StringComparison.Ordinal);
    }
}
