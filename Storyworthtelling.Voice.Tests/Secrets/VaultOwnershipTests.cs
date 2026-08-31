using Storyworthtelling.Voice.Secrets;

namespace Storyworthtelling.Voice.Tests.Secrets;

public class VaultOwnershipTests
{
    private static SecretsVault Owned(string account) =>
        SecretsVault.Empty with { OwnerAccount = account };

    // Nobody signed in: there is no question to answer yet, and an install that
    // guessed would either claim a vault it has no right to or refuse one it does.
    [Fact]
    public void NoAccount_HasNoAccessAtAll()
    {
        Assert.Equal(SharedVaultAccess.None, VaultOwnership.Decide(null, Owned("owner@example.com")));
        Assert.Equal(SharedVaultAccess.None, VaultOwnership.Decide("  ", Owned("owner@example.com")));
    }

    // A vault nobody has stamped is the first install's to claim. This is what makes
    // the whole thing settings-free: the first person to publish becomes the owner
    // by doing it, not by being told.
    [Fact]
    public void AnUnclaimedVaultIsClaimableByWhoeverIsSignedIn()
    {
        Assert.Equal(
            SharedVaultAccess.Claimable,
            VaultOwnership.Decide("someone@example.com", SecretsVault.Empty));
    }

    [Fact]
    public void TheOwnersOwnInstallMayWrite()
    {
        Assert.Equal(
            SharedVaultAccess.ReadWrite,
            VaultOwnership.Decide("owner@example.com", Owned("owner@example.com")));
    }

    // The second install reads the household's shared keys and never writes them.
    // Read-only is not a trust judgement — it is that two installs merging into one
    // file would eventually lose somebody's write, and the vault has no conflict
    // story worth the complexity for two people.
    [Fact]
    public void AnotherPersonsInstallReadsAndDoesNotWrite()
    {
        Assert.Equal(
            SharedVaultAccess.ReadOnly,
            VaultOwnership.Decide("her@example.com", Owned("owner@example.com")));
    }

    // Google hands back the address in whatever case the user typed it, and an
    // account that stopped matching itself over capitalisation would lock the owner
    // out of their own vault.
    [Fact]
    public void TheComparisonIgnoresCaseAndSurroundingSpace()
    {
        Assert.Equal(
            SharedVaultAccess.ReadWrite,
            VaultOwnership.Decide("  Owner@Example.com ", Owned("owner@example.com")));
    }

    [Fact]
    public void CanWrite_IsTrueOnlyWhereWritingIsAllowed()
    {
        Assert.True(SharedVaultAccess.ReadWrite.CanWrite());
        Assert.True(SharedVaultAccess.Claimable.CanWrite());
        Assert.False(SharedVaultAccess.ReadOnly.CanWrite());
        Assert.False(SharedVaultAccess.None.CanWrite());
    }

    // Claiming stamps the vault. Doing it as part of the ordinary publish merge is
    // what keeps it from being a separate step anyone has to remember.
    [Fact]
    public void Claim_StampsAnUnownedVault()
    {
        Assert.Equal("someone@example.com", VaultOwnership.Claim(SecretsVault.Empty, "someone@example.com").OwnerAccount);
    }

    // A vault that is already owned is never re-stamped, whoever publishes to it.
    // Silently taking ownership on a write is how the second install would quietly
    // become the first.
    [Fact]
    public void Claim_NeverTakesAVaultThatIsAlreadyOwned()
    {
        var vault = Owned("owner@example.com");

        Assert.Equal("owner@example.com", VaultOwnership.Claim(vault, "her@example.com").OwnerAccount);
    }

    [Fact]
    public void Claim_WithNoAccountLeavesTheVaultAlone()
    {
        Assert.Equal(string.Empty, VaultOwnership.Claim(SecretsVault.Empty, null).OwnerAccount);
    }
}
