using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class SecretsCryptoTests
{
    // A low iteration count keeps the tests fast; production uses SecretsCrypto.DefaultIterations.
    private const int TestIterations = 1000;

    [Fact]
    public void EncryptThenDecrypt_RoundTripsThePlaintext()
    {
        var envelope = SecretsCrypto.Encrypt("the-secret-payload", "correct horse", TestIterations);
        Assert.Equal("the-secret-payload", SecretsCrypto.Decrypt(envelope, "correct horse"));
    }

    [Fact]
    public void Encrypt_ProducesTheDocumentedEnvelopeShape()
    {
        var envelope = SecretsCrypto.Encrypt("x", "pass", TestIterations);
        Assert.Equal(SecretsEnvelope.CurrentVersion, envelope.Version);
        Assert.Equal(SecretsEnvelope.Pbkdf2Sha256, envelope.Kdf);
        Assert.Equal(TestIterations, envelope.Iterations);
        Assert.NotEmpty(envelope.Salt);
        Assert.NotEmpty(envelope.Nonce);
        Assert.NotEmpty(envelope.Ciphertext);
        Assert.NotEmpty(envelope.Tag);
    }

    [Fact]
    public void Encrypt_IsNonDeterministic_FreshSaltAndNonceEachTime()
    {
        var a = SecretsCrypto.Encrypt("same", "pass", TestIterations);
        var b = SecretsCrypto.Encrypt("same", "pass", TestIterations);
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Nonce, b.Nonce);
        Assert.NotEqual(a.Ciphertext, b.Ciphertext);
    }

    [Fact]
    public void Decrypt_WithWrongPassphrase_ThrowsWrongPassphrase()
    {
        var envelope = SecretsCrypto.Encrypt("payload", "right", TestIterations);
        Assert.Throws<WrongPassphraseException>(() => SecretsCrypto.Decrypt(envelope, "wrong"));
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_ThrowsWrongPassphrase()
    {
        var envelope = SecretsCrypto.Encrypt("payload", "pass", TestIterations);
        var bytes = Convert.FromBase64String(envelope.Ciphertext);
        bytes[0] ^= 0xFF;
        var tampered = envelope with { Ciphertext = Convert.ToBase64String(bytes) };
        Assert.Throws<WrongPassphraseException>(() => SecretsCrypto.Decrypt(tampered, "pass"));
    }

    [Fact]
    public void Encrypt_WithNoPassphrase_Throws()
    {
        Assert.Throws<ArgumentException>(() => SecretsCrypto.Encrypt("x", "", TestIterations));
    }
}

// Efficiency audit §3: each read ran PBKDF2 at 210,000 iterations. A key is now derived once per passphrase and
// salt, so a reopened file, and a file read back after it was written, cost no second derivation.
public class SecretsCryptoKeyCacheTests
{
    [Fact]
    public void AFileWrittenThenReadBackDerivesItsKeyOnce()
    {
        var before = SecretsCrypto.DerivationsOnThisThread;

        var envelope = SecretsCrypto.Encrypt("invented text", "invented passphrase key cache");
        SecretsCrypto.Decrypt(envelope, "invented passphrase key cache");
        SecretsCrypto.Decrypt(envelope, "invented passphrase key cache");

        Assert.Equal(before + 1, SecretsCrypto.DerivationsOnThisThread);
    }

    [Fact]
    public void AWrongPassphraseIsStillRefusedAfterTheRightOneWasCached()
    {
        var envelope = SecretsCrypto.Encrypt("invented text", "invented right passphrase");
        SecretsCrypto.Decrypt(envelope, "invented right passphrase");

        Assert.Throws<WrongPassphraseException>(() => SecretsCrypto.Decrypt(envelope, "invented wrong passphrase"));
    }
}
