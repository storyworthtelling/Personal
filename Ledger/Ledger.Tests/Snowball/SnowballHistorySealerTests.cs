using System.Text.Json;
using Index.Domain.Model;
using Index.Infrastructure.Snowball;
using Index.Tests.Support;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Index.Tests.Snowball;

/// <summary>The sealing half the console tool and the app share: what the tool writes is exactly what the
/// app's Drive source opens, through the same <see cref="VaultEnvelope"/> path the household file uses.</summary>
public class SnowballHistorySealerTests
{
    private const string Passphrase = "an-invented-passphrase-for-tests";

    [Fact]
    public void ASealedHistoryOpensToTheSameHistory()
    {
        var sealedText = SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase);

        var opened = SnowballHistorySealer.Open(sealedText, Passphrase);

        Assert.Equal(SnowballHistoryStatus.Loaded, opened.Status);
        Assert.Equal(
            SyntheticSnowballHistory.Rows,
            opened.History!.Rows.Select(r => (r.Loan, r.Week, r.Balance)).ToList());
    }

    [Fact]
    public void TheEnvelopeHoldsNoPlaintext_NoLoanNamesAndNoPassphrase()
    {
        var sealedText = SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase);

        Assert.DoesNotContain(Passphrase, sealedText, StringComparison.Ordinal);
        Assert.All(SyntheticSnowballHistory.LoanNames, name => Assert.DoesNotContain(name, sealedText, StringComparison.Ordinal));
        Assert.DoesNotContain("balance", sealedText, StringComparison.OrdinalIgnoreCase);
        using var envelope = JsonDocument.Parse(sealedText); // and it is the vault's envelope shape
        Assert.True(envelope.RootElement.TryGetProperty("ciphertext", out _) || envelope.RootElement.TryGetProperty("Ciphertext", out _));
    }

    [Fact]
    public void TheSameHistorySealedTwiceIsNotTheSameText()
    {
        Assert.NotEqual(
            SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase),
            SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase));
    }

    [Fact]
    public void AWrongPassphraseIsReportedAsSuchAndNothingIsRevealed()
    {
        var sealedText = SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase);

        var opened = SnowballHistorySealer.Open(sealedText, "not the passphrase");

        Assert.Equal(SnowballHistoryStatus.WrongPassphrase, opened.Status);
        Assert.Null(opened.History);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoPassphraseMeansNeedsPassphrase(string? passphrase)
    {
        var sealedText = SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase);

        Assert.Equal(SnowballHistoryStatus.NeedsPassphrase, SnowballHistorySealer.Open(sealedText, passphrase).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoContentIsAbsent(string? content) =>
        Assert.Equal(SnowballHistoryStatus.Absent, SnowballHistorySealer.Open(content, Passphrase).Status);

    [Theory]
    [InlineData("this is not an envelope")]
    [InlineData("{\"Ciphertext\":\"\"}")]
    [InlineData("{\"Version\":1,\"Ciphertext\":\"%%%\",\"Nonce\":\"%%%\",\"Salt\":\"%%%\",\"Tag\":\"%%%\"}")]
    public void ADamagedEnvelopeIsUnreadableNotAnException(string content)
    {
        var opened = SnowballHistorySealer.Open(content, Passphrase);

        Assert.Equal(SnowballHistoryStatus.Unreadable, opened.Status);
        Assert.False(string.IsNullOrWhiteSpace(opened.Reason));
    }

    [Fact]
    public void AnEnvelopeHoldingSomethingThatIsNotAHistoryIsUnreadable()
    {
        var envelope = VaultEnvelope.Write("{\"hello\":\"world\"}", Passphrase);

        var opened = SnowballHistorySealer.Open(envelope, Passphrase);

        Assert.Equal(SnowballHistoryStatus.Unreadable, opened.Status);
        Assert.DoesNotContain("hello", opened.Reason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"weeks\":[],\"loans\":[],\"rows\":[]}")]
    public void SealingMalformedInputIsRefused(string plaintext)
    {
        var exception = Assert.Throws<SnowballHistoryFormatException>(() => SnowballHistorySealer.Seal(plaintext, Passphrase));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void SealingWithNoPassphraseIsRefused(string passphrase) =>
        Assert.ThrowsAny<ArgumentException>(() => SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, passphrase));

    [Fact]
    public void SealingUsesTheVaultsOwnEnvelopeSoAVaultReaderOpensIt()
    {
        var sealedText = SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase);

        var (status, plaintext) = VaultEnvelope.Read(sealedText, Passphrase);

        Assert.Equal(SecretsVaultStatus.Loaded, status);
        Assert.Equal(SyntheticSnowballHistory.Json, plaintext);
    }
}
