using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Index.Tests.Household;

public class HouseholdSnapshotPublisherReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Passphrase = "correct horse battery staple";
    private const string OwnerEmail = "owner@example.com";

    private static (HouseholdSnapshotPublisher Publisher, HouseholdSnapshotReader Reader, InMemoryDriveTextFile File)
        Create(TestDatabase db)
    {
        var clock = new FakeClock(Now);
        var accounts = new AccountService(db, clock);
        var snowball = new SnowballService(db, clock);
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var savings = new SavingsService(db, clock);
        var file = new InMemoryDriveTextFile();

        var publisher = new HouseholdSnapshotPublisher(accounts, snowball, budget, savings, file, clock);
        var reader = new HouseholdSnapshotReader(file);
        return (publisher, reader, file);
    }

    private static async Task SeedHouseholdAsync(TestDatabase db)
    {
        await using var context = db.CreateDbContext();

        var redwood = new Institution
        {
            Name = "Redwood Bank", AccessToken = "token-redwood", CreatedAtUtc = Now.UtcDateTime,
            LastRefreshedUtc = Now, RefreshStatus = WorkStatus.Completed,
        };
        redwood.Accounts.Add(new Account
        {
            Name = "Bluebird Visa", Mask = "7306", Kind = AccountKind.Credit,
            CurrentBalance = 4_816.25m, CreditLimit = 6_000m, Apr = 0.2299m,
            NextPaymentAmount = 85m, NextPaymentDueDate = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
            IsoCurrencyCode = "USD",
        });
        redwood.Accounts.Add(new Account
        {
            Name = "Total Savings", Mask = "7302", Kind = AccountKind.Depository, Subtype = "savings",
            CurrentBalance = 8_200m, IsoCurrencyCode = "USD",
        });
        context.Institutions.Add(redwood);

        var category = new BudgetCategory { Name = "Groceries", IsActive = true, SortOrder = 0 };
        context.BudgetCategories.Add(category);
        var month = new BudgetMonth { YearMonth = "2026-09", Status = BudgetMonthStatus.Draft };
        context.BudgetMonths.Add(month);
        context.BudgetCategoryAmounts.Add(new BudgetCategoryAmount
        {
            BudgetMonthId = month.Id, CategoryId = category.Id, LimitAmount = 500m, ActualAmount = 210m,
        });

        context.SavingsGoals.Add(new SavingsGoal
        {
            AccountId = redwood.Accounts.Single(a => a.Name == "Total Savings").Id, TargetAmount = 15_000m,
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task PublishThenRead_WithTheSamePassphrase_ReturnsTheSameData()
    {
        using var db = new TestDatabase();
        await SeedHouseholdAsync(db);
        var (publisher, reader, _) = Create(db);

        var publishResult = await publisher.PublishAsync(Passphrase, OwnerEmail, default);
        Assert.True(publishResult.Ok);

        var readResult = await reader.ReadAsync(Passphrase, default);

        Assert.Equal(HouseholdSnapshotStatus.Loaded, readResult.Status);
        var snapshot = readResult.Snapshot!;
        Assert.Equal(OwnerEmail, snapshot.PublishedByAccountEmail);

        var redwood = Assert.Single(snapshot.Institutions);
        Assert.Equal("Redwood Bank", redwood.Name);
        Assert.False(redwood.IsStale);
        Assert.Equal(2, redwood.Accounts.Count);
        var card = redwood.Accounts.Single(a => a.Name == "Bluebird Visa");
        Assert.Equal(4_816.25m, card.CurrentBalance);
        Assert.Equal(6_000m, card.CreditLimit);
        Assert.Equal(85m, card.NextPaymentAmount);

        var savingsAccount = Assert.Single(snapshot.Savings.Accounts);
        Assert.Equal(8_200m, savingsAccount.Balance);
        Assert.Equal(15_000m, savingsAccount.GoalTargetAmount);

        var groceries = Assert.Single(snapshot.Budget.Categories);
        Assert.Equal("Groceries", groceries.Name);
        Assert.Equal(500m, groceries.LimitAmount);
        Assert.Equal(210m, groceries.ActualAmount);
    }

    [Fact]
    public async Task Read_WithAWrongPassphrase_ThrowsNeitherAndReportsWrongPassphrase()
    {
        using var db = new TestDatabase();
        await SeedHouseholdAsync(db);
        var (publisher, reader, _) = Create(db);

        await publisher.PublishAsync(Passphrase, OwnerEmail, default);

        // The same WrongPassphraseException path the shared vault itself exercises (VaultEnvelope.Read
        // catches it internally) — this must never propagate as an unhandled exception to a caller.
        var readResult = await reader.ReadAsync("the wrong passphrase entirely", default);

        Assert.Equal(HouseholdSnapshotStatus.WrongPassphrase, readResult.Status);
        Assert.Null(readResult.Snapshot);
    }

    [Fact]
    public async Task Read_BeforeAnythingHasEverBeenPublished_ReportsEmpty()
    {
        using var db = new TestDatabase();
        var (_, reader, _) = Create(db);

        var readResult = await reader.ReadAsync(Passphrase, default);

        Assert.Equal(HouseholdSnapshotStatus.Empty, readResult.Status);
    }

    [Fact]
    public async Task Publish_WithNoPassphraseSet_IsSkippedNotFailed()
    {
        using var db = new TestDatabase();
        await SeedHouseholdAsync(db);
        var (publisher, _, file) = Create(db);

        var result = await publisher.PublishAsync(string.Empty, OwnerEmail, default);

        Assert.True(result.Ok); // a skip is not a failure — best-effort, never blocks the refresh cycle
        Assert.Null(file.Content);
    }

    [Fact]
    public async Task PaidOffAccountsAndBanksWithNothingOpen_AreExcluded_MatchingD124()
    {
        using var db = new TestDatabase();
        await using (var context = db.CreateDbContext())
        {
            var settled = new Institution { Name = "Northfield Student Loans", AccessToken = "tok", CreatedAtUtc = Now.UtcDateTime };
            settled.Accounts.Add(new Account { Name = "Northfield Student Loans 1", Kind = AccountKind.Loan, CurrentBalance = 0m });
            context.Institutions.Add(settled);
            await context.SaveChangesAsync();
        }
        var (publisher, reader, _) = Create(db);

        await publisher.PublishAsync(Passphrase, OwnerEmail, default);
        var readResult = await reader.ReadAsync(Passphrase, default);

        Assert.Empty(readResult.Snapshot!.Institutions);
    }
}
