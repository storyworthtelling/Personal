namespace Storyworthtelling.Diagnostics.Tests;

// A4/§6.2: the reader holds a key derived from the vault passphrase, never the passphrase itself.
// The expected values come from an independent implementation (Python's hashlib.pbkdf2_hmac, sha256,
// salt "storyworthtelling-log-key/<epoch>", 210,000 iterations, 32 bytes), not from this code.
public class LogKeyDeriveTests
{
    private const string Passphrase = "correct horse battery staple";

    [Fact]
    public void AFixedPassphraseAndEpochGiveAFixedKey()
    {
        Assert.Equal(
            "623460204d14e349fe24d1ef58941a386f4b04c6d8cfbf18ace05ba6b556d6fb",
            LogKey.Derive(Passphrase, 1));
    }

    [Fact]
    public void ADifferentEpochGivesADifferentKey()
    {
        Assert.Equal(
            "6a607ea6923d0b9981b76542fcb60738f7db3bed7ff68c84866723e5cab3e194",
            LogKey.Derive(Passphrase, 2));
        Assert.NotEqual(LogKey.Derive(Passphrase, 1), LogKey.Derive(Passphrase, 2));
    }

    [Fact]
    public void TheIterationCountMatchesTheVaults()
    {
        Assert.Equal(210_000, LogKey.Iterations);
        Assert.Equal("storyworthtelling-log-key/", LogKey.SaltPrefix);
    }

    [Fact]
    public void TheEpochStartsAtOne()
    {
        Assert.Equal(1, LogKey.Epoch);
    }

    [Fact]
    public void AnEmptyPassphraseIsRefusedRatherThanDerivingAKeyAnyoneCouldGuess()
    {
        Assert.Throws<ArgumentException>(() => LogKey.Derive(string.Empty, 1));
        Assert.Throws<ArgumentException>(() => LogKey.Derive("   ", 1));
    }

    [Fact]
    public void TheKeyIsNeverThePassphrase()
    {
        Assert.DoesNotContain(Passphrase, LogKey.Derive(Passphrase, 1));
    }
}
