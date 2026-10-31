using Constitution.Infrastructure.Household;
using Storyworthtelling.Household;
using Storyworthtelling.Voice.Secrets;

namespace Constitution.Tests.HouseholdHead;

public class HouseholdRoleReaderTests
{
    private sealed class FixedVault(SecretsVaultLoadResult result) : ISecretsVaultStore
    {
        public int Writes { get; private set; }

        public Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken) => Task.FromResult(result);

        public Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }
    }

    private static SecretsVault OwnedBy(string account) => SecretsVault.Empty with { OwnerAccount = account };

    private static Task<HouseholdRoleRead> Read(SecretsVaultLoadResult result, string? passphrase = "invented-passphrase", string? account = "sam@example.com") =>
        HouseholdRoleReader.ReadAsync(new FixedVault(result), passphrase, account, CancellationToken.None);

    [Fact]
    public async Task TheVaultsOwnerIsTheOwner()
    {
        var read = await Read(SecretsVaultLoadResult.Loaded(OwnedBy("Sam@Example.com")));

        Assert.Equal(HouseholdFileRole.Owner, read.Role);
    }

    [Fact]
    public async Task AnotherAccountIsSharedWith()
    {
        var read = await Read(SecretsVaultLoadResult.Loaded(OwnedBy("lee@example.com")));

        Assert.Equal(HouseholdFileRole.SharedWith, read.Role);
    }

    [Fact]
    public async Task AnUnclaimedVaultIsClaimableSoOwner()
    {
        var read = await Read(SecretsVaultLoadResult.Loaded(SecretsVault.Empty));

        Assert.Equal(HouseholdFileRole.Owner, read.Role);
    }

    [Theory]
    [InlineData(SecretsVaultStatus.Empty)]
    [InlineData(SecretsVaultStatus.WrongPassphrase)]
    [InlineData(SecretsVaultStatus.Corrupt)]
    public async Task AVaultNotReadSaysNothing(SecretsVaultStatus status)
    {
        var read = await Read(new SecretsVaultLoadResult(status, SecretsVault.Empty));

        Assert.Equal(HouseholdFileRole.Unknown, read.Role);
    }

    [Fact]
    public async Task NoPassphraseOrAccountReadsNothing()
    {
        var store = new FixedVault(SecretsVaultLoadResult.Loaded(OwnedBy("sam@example.com")));

        var none = await HouseholdRoleReader.ReadAsync(store, string.Empty, "sam@example.com", CancellationToken.None);
        var anon = await HouseholdRoleReader.ReadAsync(store, "invented-passphrase", null, CancellationToken.None);

        Assert.Equal(HouseholdFileRole.Unknown, none.Role);
        Assert.Equal(HouseholdFileRole.Unknown, anon.Role);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task TheReaderNeverWritesTheVault()
    {
        var store = new FixedVault(SecretsVaultLoadResult.Loaded(SecretsVault.Empty));

        await HouseholdRoleReader.ReadAsync(store, "invented-passphrase", "sam@example.com", CancellationToken.None);

        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task TheVaultsFileIdsComeBackForTheDriveLookup()
    {
        var vault = OwnedBy("sam@example.com") with { DriveFileIds = new Dictionary<string, string> { ["example-file"] = "id-1" } };

        var read = await Read(SecretsVaultLoadResult.Loaded(vault));

        Assert.Equal("id-1", read.DriveFileIds["example-file"]);
    }
}
