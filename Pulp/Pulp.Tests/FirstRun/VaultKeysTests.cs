using Pulp.Domain.FirstRun;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// The three keys the Vault page lists (D285: Streaming Availability direct, the rating source, the
/// trailer source), how an entry merges into the vault's Pulp section, and when a rejection still
/// stands. Key values here are invented placeholders, never the shape of a real key.
/// </summary>
public class VaultKeysTests
{
    private static readonly DateTimeOffset Checked = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ThePageListsThePulpCredentialsInOrder() =>
        Assert.Equal(
            [PulpCredentialIds.StreamingAvailability, PulpCredentialIds.Omdb, PulpCredentialIds.YouTube],
            VaultKeys.All.Select(key => key.CredentialId));

    [Fact]
    public void EveryKeyHasANameForThePage() =>
        Assert.All(VaultKeys.All, key => Assert.False(string.IsNullOrWhiteSpace(key.Name)));

    [Fact]
    public void ABlankEntryKeepsTheKeyTheVaultAlreadyHolds() =>
        Assert.Equal("held-example", VaultKeys.Merge("held-example", "   "));

    [Fact]
    public void ANewEntryReplacesTheKeyTrimmed() =>
        Assert.Equal("new-example", VaultKeys.Merge("held-example", "  new-example \n"));

    [Fact]
    public void ARejectionStandsUntilTheKeyIsReplaced() =>
        Assert.True(VaultKeys.StillRejected(failing: true, checkedAt: Checked, replacedAt: null));

    /// <summary>
    /// Health is only written by a live call, so a key replaced after its rejection would otherwise
    /// keep showing "turned down" until the next lookup — on the page the owner just fixed it on.
    /// </summary>
    [Fact]
    public void AKeyReplacedAfterItsRejectionIsNoLongerRejected() =>
        Assert.False(VaultKeys.StillRejected(failing: true, checkedAt: Checked, replacedAt: Checked.AddMinutes(1)));

    [Fact]
    public void ARejectionAfterTheReplacementStands() =>
        Assert.True(VaultKeys.StillRejected(failing: true, checkedAt: Checked.AddMinutes(2), replacedAt: Checked));

    [Fact]
    public void AKeyNotFailingIsNotRejected() =>
        Assert.False(VaultKeys.StillRejected(failing: false, checkedAt: Checked, replacedAt: null));

    [Fact]
    public void TheRejectedSentenceNamesTheKeyPlainly()
    {
        var sentence = VaultKeys.Rejected("OMDb");

        Assert.Contains("OMDb", sentence);
        Assert.EndsWith(".", sentence);
        Assert.DoesNotContain("401", sentence);
    }
}
