using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// Audit 11 §13: restore was "local wins", so once the owner re-paired the tablet or rotated a key in one app, every
/// other app kept its dead copy forever. A vault value now replaces a local one whose last use was refused.
/// </summary>
public class VaultRestoreTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 13, 7, 0, 0, TimeSpan.Zero);
    private static readonly CredentialHealth Refused = CredentialHealth.Failing("401", At);

    [Fact]
    public void AnEmptyLocalSlotTakesTheVaultValue() =>
        Assert.Equal("vault", VaultRestore.Take(local: "", vault: "vault", CredentialHealth.Unknown));

    [Fact]
    public void AWorkingLocalValueStays() =>
        Assert.Null(VaultRestore.Take(local: "local", vault: "vault", CredentialHealth.Ok(At)));

    [Fact]
    public void AnUntriedLocalValueStays() =>
        Assert.Null(VaultRestore.Take(local: "local", vault: "vault", CredentialHealth.Unknown));

    [Fact]
    public void ARefusedLocalValueYieldsToADifferentVaultValue() =>
        Assert.Equal("vault", VaultRestore.Take(local: "local", vault: "vault", Refused));

    [Fact]
    public void ARefusedLocalValueTheVaultAlsoHoldsIsNotTakenAgain() =>
        Assert.Null(VaultRestore.Take(local: "same", vault: "same", Refused));

    [Fact]
    public void ABlankVaultNeverClearsALocalValue() =>
        Assert.Null(VaultRestore.Take(local: "local", vault: " ", Refused));

    [Fact]
    public void ForgettingAHealthEntryLeavesItUnknown()
    {
        var report = CredentialHealthReport.Empty.MarkFailing("remarkable", "401", At).Forget("remarkable");

        Assert.Equal(CredentialStatus.Unknown, report.Get("remarkable").Status);
    }
}
