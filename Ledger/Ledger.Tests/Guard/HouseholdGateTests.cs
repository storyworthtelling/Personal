using System.Text.Json;
using Index.Domain.External;
using Index.Domain.Household;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Index.Infrastructure.Vault;
using Index.Tests.Support;
using Moq;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Phase 1.1 of the remediation plan: her install must never touch Plaid or the owner's vault
/// section. R-G1 and R-G5 are source-level and live in <see cref="HouseholdGateSourceTests"/>; the
/// pure halves of R-G2, R-G3 and R-G4 are here, tested directly rather than through the Android head.
/// </summary>
public class HouseholdGateTests
{
    private const string Passphrase = "correct horse battery staple";
    private const string OwnerEmail = "owner@example.com";
    private const string HerEmail = "her@example.com";
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);

    // ---- R-G2: restore and scheduling are skipped when another account published the file ----

    private sealed class MemoryCache : IInstallRoleCache
    {
        public InstallRole? Value { get; set; }
        public InstallRole? Get() => Value;
        public void Set(InstallRole role) => Value = role;
        public void Clear() => Value = null;
    }

    private sealed class FakeLookup(HouseholdFilePublisher answer) : IHouseholdPublisherLookup
    {
        public int Calls { get; private set; }
        public HouseholdFilePublisher Answer { get; set; } = answer;

        public Task<HouseholdFilePublisher> LookAsync(string passphrase, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Answer);
        }
    }

    private static InstallRoleService Service(
        MemoryCache cache, FakeLookup lookup, string? account = HerEmail, string? passphrase = Passphrase) =>
        new(cache, lookup, () => account, () => passphrase);

    [Fact]
    public async Task AFilePublishedByAnotherAccountMeansHersAndTheLaunchDoesNothing()
    {
        var service = Service(new MemoryCache(), new FakeLookup(HouseholdFilePublisher.PublishedBy(OwnerEmail)));

        var role = await service.ResolveAsync(default);
        var plan = LaunchPlan.For(role);

        Assert.Equal(InstallRole.Hers, role);
        Assert.False(plan.RestoreFromVault);
        Assert.False(plan.ScheduleRefresh);
        Assert.False(plan.RunRefreshNow);
        Assert.False(plan.ScheduleWorksheet);
    }

    [Fact]
    public async Task AFilePublishedByThisAccountMeansOwnerAndTheLaunchIsUnchanged()
    {
        var service = Service(
            new MemoryCache(), new FakeLookup(HouseholdFilePublisher.PublishedBy(OwnerEmail)), account: "Owner@Example.com");

        var plan = LaunchPlan.For(await service.ResolveAsync(default));

        Assert.True(plan.RestoreFromVault);
        Assert.True(plan.ScheduleRefresh);
        Assert.True(plan.RunRefreshNow);
        Assert.True(plan.ScheduleWorksheet);
    }

    [Fact]
    public async Task NoFileYetIsTheOwnersBehaviourAndIsNotRemembered()
    {
        var cache = new MemoryCache();
        var service = Service(cache, new FakeLookup(HouseholdFilePublisher.Absent));

        var role = await service.ResolveAsync(default);

        Assert.Equal(InstallRole.Owner, role);
        Assert.True(LaunchPlan.For(role).RestoreFromVault);
        Assert.Null(cache.Value);
        Assert.False(service.IsResolved);
    }

    [Theory]
    [InlineData(null, Passphrase)]
    [InlineData(HerEmail, null)]
    [InlineData(HerEmail, "")]
    public async Task WithoutBothAnAccountAndAPassphraseNothingIsReadOrRemembered(string? account, string? passphrase)
    {
        var cache = new MemoryCache();
        var lookup = new FakeLookup(HouseholdFilePublisher.PublishedBy(OwnerEmail));

        var role = await Service(cache, lookup, account, passphrase).ResolveAsync(default);

        Assert.Equal(InstallRole.Owner, role);
        Assert.Equal(0, lookup.Calls);
        Assert.Null(cache.Value);
    }

    [Fact]
    public async Task AnUnreadableFileIsNotRememberedAndKeepsItsReason()
    {
        var cache = new MemoryCache();
        var service = Service(cache, new FakeLookup(HouseholdFilePublisher.Unreadable("Drive refused.")));

        var role = await service.ResolveAsync(default);

        Assert.Equal(InstallRole.Owner, role);
        Assert.Null(cache.Value);
        Assert.Equal("Drive refused.", service.UnresolvedReason);
    }

    [Fact]
    public async Task OnceHersIsRememberedDriveIsNeverAskedAgainAndAFlakyReadCannotFlipIt()
    {
        var cache = new MemoryCache();
        var lookup = new FakeLookup(HouseholdFilePublisher.PublishedBy(OwnerEmail));
        var service = Service(cache, lookup);

        await service.ResolveAsync(default);
        lookup.Answer = HouseholdFilePublisher.Unreadable("offline");
        var again = await service.ResolveAsync(default);

        Assert.Equal(InstallRole.Hers, again);
        Assert.Equal(1, lookup.Calls);
        Assert.Equal(InstallRole.Hers, service.Current);
    }

    [Fact]
    public async Task ChangingTheAccountOrPassphraseForgetsTheRememberedRole()
    {
        var cache = new MemoryCache { Value = InstallRole.Hers };
        var lookup = new FakeLookup(HouseholdFilePublisher.PublishedBy(HerEmail));
        var service = Service(cache, lookup);

        service.Invalidate();
        var role = await service.ResolveAsync(default);

        Assert.Equal(InstallRole.Owner, role);
        Assert.Equal(1, lookup.Calls);
    }

    // ---- R-G3: the publisher never takes over a file another account published ----

    private static (HouseholdSnapshotPublisher Publisher, HouseholdSnapshotReader Reader, InMemoryDriveTextFile File)
        CreatePublisher(TestDatabase db, InMemoryDriveTextFile? file = null)
    {
        var clock = new FakeClock(Now);
        file ??= new InMemoryDriveTextFile();
        var publisher = new HouseholdSnapshotPublisher(
            new AccountService(db, clock), new SnowballService(db, clock),
            new BudgetService(db, clock, new Mock<IPlaidClient>().Object), new SavingsService(db, clock),
            file, clock);
        return (publisher, new HouseholdSnapshotReader(file), file);
    }

    [Fact]
    public async Task ThePublisherRefusesAFileAnotherAccountPublished()
    {
        using var db = new TestDatabase();
        var (publisher, reader, file) = CreatePublisher(db);
        Assert.True((await publisher.PublishAsync(Passphrase, OwnerEmail, default)).Ok);
        var ownersFile = file.Content;

        var hers = await publisher.PublishAsync(Passphrase, HerEmail, default);

        Assert.Equal(ownersFile, file.Content);
        Assert.True(hers.Ok);
        Assert.NotNull(hers.Message);
        var read = await reader.ReadAsync(Passphrase, default);
        Assert.Equal(OwnerEmail, read.Snapshot!.PublishedByAccountEmail);
    }

    [Fact]
    public async Task ThePublisherStillRefreshesItsOwnFileAndWritesWhenThereIsNone()
    {
        using var db = new TestDatabase();
        var (publisher, reader, file) = CreatePublisher(db);

        Assert.Null(file.Content);
        Assert.Null((await publisher.PublishAsync(Passphrase, OwnerEmail, default)).Message);
        var first = file.Content;

        // The same account, differently capitalised, is still the same account.
        Assert.Null((await publisher.PublishAsync(Passphrase, "OWNER@example.com", default)).Message);

        Assert.NotNull(first);
        Assert.Equal(HouseholdSnapshotStatus.Loaded, (await reader.ReadAsync(Passphrase, default)).Status);
    }

    [Fact]
    public async Task ThePublisherDoesNotReplaceAFileItCannotOpen()
    {
        using var db = new TestDatabase();
        var (publisher, _, file) = CreatePublisher(db);
        await publisher.PublishAsync("someone else's passphrase", OwnerEmail, default);
        var theirs = file.Content;

        var result = await publisher.PublishAsync(Passphrase, HerEmail, default);

        Assert.Equal(theirs, file.Content);
        Assert.True(result.Ok);
        Assert.NotNull(result.Message);
    }

    // ---- R-G4: a Save with no local credentials leaves the vault's Plaid section alone ----

    private sealed class MemoryVaultStore : ISecretsVaultStore
    {
        public SecretsVault Vault { get; set; } = SecretsVault.Empty;

        // The vault file's Drive ownership, which is what decides who may write it.
        public DriveFileOwnership Ownership { get; set; } = DriveFileOwnership.Owned;

        public Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken) =>
            Task.FromResult(new SecretsVaultLoadResult(SecretsVaultStatus.Loaded, Vault, Ownership));

        public Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken)
        {
            Vault = vault;
            return Task.CompletedTask;
        }
    }

    private static SecretsVault OwnersVault() => SecretsVault.Empty with
    {
        OwnerAccount = OwnerEmail,
        Plaid = new PlaidSecrets("client-id", "client-secret", "production")
        {
            Items = [new PlaidItem("item-1", "access-token-1", "Redwood Bank"), new PlaidItem("item-2", "access-token-2", "Ally")],
        },
    };

    private static string PlaidJson(SecretsVault vault) =>
        JsonSerializer.Serialize(vault.Plaid, VaultEnvelope.SerializerOptions);

    [Fact]
    public async Task ASaveWithNoLocalCredentialsLeavesThePlaidSectionByteIdentical()
    {
        var store = new MemoryVaultStore { Vault = OwnersVault() };
        var before = PlaidJson(store.Vault);
        var service = new SecretsVaultService(store, () => Passphrase);

        // Exactly what her install's VaultSync would publish: blank local credentials, no banks.
        var result = await service.PublishAsync(
            v => PlaidVaultMerge.Apply(v, string.Empty, string.Empty, "production", []), default);

        Assert.True(result.Succeeded);
        Assert.Equal(before, PlaidJson(store.Vault));
    }

    [Fact]
    public void ABlankFieldNeverOverwritesANonBlankOneButRealCredentialsAreWritten()
    {
        var vault = OwnersVault();

        var rotatedSecret = PlaidVaultMerge.Apply(
            vault, string.Empty, "new-secret", "production", [new PlaidItem("item-3", "access-token-3", "Wells")]);

        Assert.Equal("client-id", rotatedSecret.Plaid.ClientId);
        Assert.Equal("new-secret", rotatedSecret.Plaid.Secret);
        Assert.Equal("item-3", Assert.Single(rotatedSecret.Plaid.Items).ItemId);
    }

    [Fact]
    public void RemovingTheLastBankStillPropagatesWhenThisInstallHoldsCredentials()
    {
        var merged = PlaidVaultMerge.Apply(OwnersVault(), "client-id", "client-secret", "production", []);

        Assert.Empty(merged.Plaid.Items);
        Assert.Equal("client-id", merged.Plaid.ClientId);
    }
}
