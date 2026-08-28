using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class TidalPairingTests
{
    // Every code the token endpoint sends while a device code is still waiting on the
    // owner to approve it in their browser — RFC 8628's error vocabulary, which TIDAL
    // follows. The polling loop and its tests have to agree on what each one means
    // without either depending on the network, which is what this maps for them.
    [Theory]
    [InlineData("authorization_pending", TidalPairingSignal.Pending)]
    [InlineData("slow_down", TidalPairingSignal.SlowDown)]
    [InlineData("access_denied", TidalPairingSignal.Denied)]
    [InlineData("expired_token", TidalPairingSignal.Expired)]
    public void Interpret_MapsEveryDocumentedErrorCode(string errorCode, TidalPairingSignal expected)
    {
        Assert.Equal(expected, TidalPairing.Interpret(errorCode));
    }

    // A code TIDAL adds later, or any other body shape, is not a state the pairing
    // screen can act on — it is not Pending, so polling must not loop on it forever.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something_new")]
    public void Interpret_TreatsAnythingElseAsUnknown(string? errorCode)
    {
        Assert.Equal(TidalPairingSignal.Unknown, TidalPairing.Interpret(errorCode));
    }
}
