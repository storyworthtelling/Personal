using Index.Domain.External;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Index.Tests.Household;

/// <summary>
/// Remediation 3.3 items 5 and 7, on HER install. Her Accounts page is drawn from the household file, which
/// carried neither a depository account's subtype (so her rows read "Account ····7301", not "Checking ····7301")
/// nor whether a bank was typed in by hand (so her heading never said "By hand"). Both ride as ADDITIVE,
/// OPTIONAL fields — no version bump (remediation 3.6's rule): an older file reads as "none", and an older build
/// reading a newer file ignores them.
/// </summary>
public class HouseholdAccountLabelsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Passphrase = "correct horse battery staple";
    private const string OwnerEmail = "owner@example.com";

    private static (HouseholdSnapshotPublisher Publisher, HouseholdSnapshotReader Reader, InMemoryDriveTextFile File) Create(TestDatabase db)
    {
        var clock = new FakeClock(Now);
        var file = new InMemoryDriveTextFile();
        var publisher = new HouseholdSnapshotPublisher(
            new AccountService(db, clock), new SnowballService(db, clock),
            new BudgetService(db, clock, new Mock<IPlaidClient>().Object), new SavingsService(db, clock), file, clock);
        return (publisher, new HouseholdSnapshotReader(file), file);
    }

    private static async Task SeedAsync(TestDatabase db)
    {
        await using var context = db.CreateDbContext();
        var redwood = new Institution
        {
            Name = "Redwood Bank", AccessToken = "token-redwood", CreatedAtUtc = Now, LastRefreshedUtc = Now,
            RefreshStatus = WorkStatus.Completed, SortOrder = 0,
        };
        redwood.Accounts.Add(new Account
        {
            Name = "Everyday Checking", Mask = "7301", Kind = AccountKind.Depository, Subtype = "checking",
            CurrentBalance = 3_150.25m, IsoCurrencyCode = "USD",
        });
        context.Institutions.Add(redwood);

        var evergreenHome = new Institution
        {
            Name = "Evergreen Home Lending", AccessToken = string.Empty, CreatedAtUtc = Now, LastRefreshedUtc = Now,
            RefreshStatus = WorkStatus.Completed, SortOrder = 1,
        };
        evergreenHome.Accounts.Add(new Account
        {
            Name = "Car loan", Kind = AccountKind.Loan, Source = AccountSource.Manual,
            CurrentBalance = 12_345.67m, IsoCurrencyCode = "USD",
        });
        context.Institutions.Add(evergreenHome);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ThePublishedFileCarriesTheSubtypeAndWhetherABankWasTypedInByHand()
    {
        using var db = new TestDatabase();
        await SeedAsync(db);
        var (publisher, reader, _) = Create(db);

        Assert.True((await publisher.PublishAsync(Passphrase, OwnerEmail, default)).Ok);
        var snapshot = (await reader.ReadAsync(Passphrase, default)).Snapshot!;

        var redwood = snapshot.Institutions.Single(i => i.Name == "Redwood Bank");
        Assert.False(redwood.IsHandEntered);
        Assert.Equal("checking", redwood.Accounts.Single().Subtype);

        var evergreenHome = snapshot.Institutions.Single(i => i.Name == "Evergreen Home Lending");
        Assert.True(evergreenHome.IsHandEntered);
        Assert.Null(evergreenHome.Accounts.Single().Subtype);
    }

    [Fact]
    public async Task ThePublishedFileStaysVersionTwo_TheFieldsAreAdditive()
    {
        using var db = new TestDatabase();
        await SeedAsync(db);
        var (publisher, reader, _) = Create(db);

        await publisher.PublishAsync(Passphrase, OwnerEmail, default);
        var snapshot = (await reader.ReadAsync(Passphrase, default)).Snapshot!;

        Assert.Equal(HouseholdSnapshot.CurrentVersion, snapshot.Version);
        Assert.Equal(2, HouseholdSnapshot.CurrentVersion);
    }

    [Fact]
    public async Task AFileWrittenBeforeTheFieldsExistedReadsAsNone()
    {
        var file = new InMemoryDriveTextFile();
        await file.WriteAsync(VaultEnvelope.Write(HouseholdV1Fixture.Json, Passphrase), default);

        var snapshot = (await new HouseholdSnapshotReader(file).ReadAsync(Passphrase, default)).Snapshot!;

        Assert.All(snapshot.Institutions, i => Assert.False(i.IsHandEntered));
        Assert.All(snapshot.Institutions.SelectMany(i => i.Accounts), a => Assert.Null(a.Subtype));
        Assert.Equal("Account", AccountCardText.KindLabel(AccountKind.Depository, snapshot.Institutions[0].Accounts[0].Subtype));
    }

    [Fact]
    public void TheNewMembersAreOptionalConstructorParameters_SoNothingElseHadToChange()
    {
        // Constructing the records the way every existing caller does (without the new members) still works.
        var account = new HouseholdAccount("Everyday Checking", "7301", AccountKind.Depository, 1m, null, null, null, "USD");
        var institution = new HouseholdInstitution("Redwood Bank", false, null, [account]);

        Assert.Null(account.Subtype);
        Assert.False(institution.IsHandEntered);
    }
}
