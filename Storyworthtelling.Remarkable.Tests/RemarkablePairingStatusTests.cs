using Storyworthtelling.Voice.Health;
using Xunit;

namespace Storyworthtelling.Remarkable.Tests;

/// <summary>
/// Audit 11 §11: Foreword said "Paired" and Folio "Registered ✓" whenever a token was stored, so a pairing the
/// reMarkable cloud had revoked read as working forever. The line now follows the pairing's health.
/// </summary>
public class RemarkablePairingStatusTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 13, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoTokenIsNotPaired()
    {
        var status = RemarkablePairingStatus.Describe(paired: false, CredentialHealth.Unknown, "Not paired yet.");

        Assert.Equal("Not paired yet.", status.Line);
        Assert.Equal("Pair", status.Button);
        Assert.True(status.NeedsPairing);
    }

    [Fact]
    public void ARevokedPairingSaysSoEvenWithATokenStored()
    {
        var refused = CredentialHealthReport.Empty.MarkFailing(RemarkableCredentialId.Value, "401", At).Get(RemarkableCredentialId.Value);

        var status = RemarkablePairingStatus.Describe(paired: true, refused, "Not paired yet.");

        Assert.Equal("The reMarkable turned this phone's pairing away. Pair again.", status.Line);
        Assert.Equal("Pair again", status.Button);
        Assert.True(status.NeedsPairing);
    }

    [Fact]
    public void AWorkingPairingIsTicked()
    {
        var working = CredentialHealthReport.Empty.MarkOk(RemarkableCredentialId.Value, At).Get(RemarkableCredentialId.Value);

        var status = RemarkablePairingStatus.Describe(paired: true, working, "Not paired yet.");

        Assert.Equal("Paired ✓", status.Line);
        Assert.Equal("Re-pair", status.Button);
        Assert.False(status.NeedsPairing);
    }

    [Fact]
    public void AStoredPairingNotYetUsedClaimsNothingMore()
    {
        var status = RemarkablePairingStatus.Describe(paired: true, CredentialHealth.Unknown, "Not paired yet.");

        Assert.Equal("Paired", status.Line);
        Assert.False(status.NeedsPairing);
    }
}
