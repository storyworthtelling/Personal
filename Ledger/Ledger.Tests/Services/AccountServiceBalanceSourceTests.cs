using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Configuration;
using Index.Infrastructure.Persistence;
using Index.Infrastructure.Pipeline;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Services;

/// <summary>
/// Remediation 3.3 item 3: who WRITES <see cref="Account.BalanceSource"/>. A Post receipt marks the balance as
/// Post's; a number the owner types marks it as theirs; a Plaid refresh puts it back to Plaid's (so a stale
/// connection that Post covered for does not keep saying "From Post" after it reconnects). A receipt's
/// date is stored as a date — UTC midnight — whichever way it arrives.
/// </summary>
public class AccountServiceBalanceSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Account> LoadAsync(TestDatabase db, Guid id)
    {
        await using var context = db.CreateDbContext();
        return await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    [Fact]
    public async Task AHandEnteredAccountStartsAsByHand()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));

        var account = await service.AddManualAccountAsync(
            "Car loan", "Evergreen Home Lending", AccountKind.Loan, 12_345.67m,
            apr: null, payment: null, cadence: null, nextDueUtc: null, default);

        Assert.Equal(BalanceSource.ByHand, (await LoadAsync(db, account.Id)).BalanceSource);
    }

    [Fact]
    public async Task APostReceiptMarksTheBalanceAsFromPost_AndStoresItsDateAsADate()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));
        var account = await service.AddManualAccountAsync(
            "Car loan", "Evergreen Home Lending", AccountKind.Loan, 12_345.67m,
            apr: null, payment: null, cadence: null, nextDueUtc: null, default);

        var applied = await service.ApplyBalanceReceiptAsync("Evergreen Home Lending", 11_000.00m, new DateOnly(2026, 9, 12), default);

        Assert.True(applied);
        var stored = await LoadAsync(db, account.Id);
        Assert.Equal(BalanceSource.Post, stored.BalanceSource);
        Assert.Equal(11_000.00m, stored.CurrentBalance);
        Assert.Equal(new DateOnly(2026, 9, 12), CalendarDate.Of(stored.BalanceAsOfUtc!.Value));
        Assert.Equal("From Post, Sep 12", BalanceDisplay.UpdatedLine(stored, Now));
    }

    [Fact]
    public async Task AReceiptWithNoStatementDateIsDatedTodayAsADate_NotAsTheUtcInstantOfNow()
    {
        // 02:00 UTC on Sep 27 is still the evening of Sep 26 in the US. A receipt that arrives then must read
        // "From Post, Sep 26" on that phone — so it stores the phone's day, not UTC's.
        var lateUtc = new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero);
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(lateUtc));
        var account = await service.AddManualAccountAsync(
            "Car loan", "Evergreen Home Lending", AccountKind.Loan, 12_345.67m,
            apr: null, payment: null, cadence: null, nextDueUtc: null, default);

        await service.ApplyBalanceReceiptAsync("Evergreen Home Lending", 11_000.00m, asOf: null, default);

        var stored = await LoadAsync(db, account.Id);
        var phoneToday = DateOnly.FromDateTime(lateUtc.LocalDateTime);
        Assert.Equal(phoneToday, CalendarDate.Of(stored.BalanceAsOfUtc!.Value));
        Assert.Equal(TimeSpan.Zero, stored.BalanceAsOfUtc!.Value.TimeOfDay); // a date, not an instant
    }

    [Fact]
    public async Task ATypedBalanceTakesTheAccountBackToByHand()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));
        var account = await service.AddManualAccountAsync(
            "Car loan", "Evergreen Home Lending", AccountKind.Loan, 12_345.67m,
            apr: null, payment: null, cadence: null, nextDueUtc: null, default);
        await service.ApplyBalanceReceiptAsync("Evergreen Home Lending", 11_000.00m, new DateOnly(2026, 9, 12), default);

        await service.UpdateManualAccountAsync(account.Id, new ManualAccountPatch { Balance = 10_000m }, default);

        Assert.Equal(BalanceSource.ByHand, (await LoadAsync(db, account.Id)).BalanceSource);
    }

    [Fact]
    public async Task EditingSomethingOtherThanTheBalanceLeavesThePostSourceAlone()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));
        var account = await service.AddManualAccountAsync(
            "Car loan", "Evergreen Home Lending", AccountKind.Loan, 12_345.67m,
            apr: null, payment: null, cadence: null, nextDueUtc: null, default);
        await service.ApplyBalanceReceiptAsync("Evergreen Home Lending", 11_000.00m, new DateOnly(2026, 9, 12), default);

        await service.UpdateManualAccountAsync(account.Id, new ManualAccountPatch { Apr = 6.25m }, default);

        var stored = await LoadAsync(db, account.Id);
        Assert.Equal(BalanceSource.Post, stored.BalanceSource);
        Assert.Equal(new DateOnly(2026, 9, 12), CalendarDate.Of(stored.BalanceAsOfUtc!.Value));
    }

    [Fact]
    public async Task APlaidRefreshTakesABalanceBackToPlaid()
    {
        using var db = new TestDatabase();
        var clock = new FakeClock(Now);
        var service = new AccountService(db, clock);

        // A connected, currently broken bank that Post covered for.
        var institution = await service.AddInstitutionAsync("item-d", "access-d", "Meridian Card", default);
        Guid accountId;
        await using (var setup = db.CreateDbContext())
        {
            var row = await setup.Institutions.FindAsync(institution.Id);
            row!.RefreshStatus = WorkStatus.Failed;
            var account = new Account
            {
                InstitutionId = row.Id, AccountId = "acc-1", Name = "Meridian Plus",
                Kind = AccountKind.Credit, CurrentBalance = 10m,
            };
            setup.Accounts.Add(account);
            await setup.SaveChangesAsync();
            accountId = account.Id;
        }
        await service.ApplyBalanceReceiptAsync("Meridian Card", 5_908.30m, new DateOnly(2026, 9, 12), default);
        Assert.Equal(BalanceSource.Post, (await LoadAsync(db, accountId)).BalanceSource);

        // It reconnects, and the next refresh brings Plaid's own figure.
        await using (var reconnect = db.CreateDbContext())
        {
            var row = await reconnect.Institutions.FindAsync(institution.Id);
            row!.RefreshStatus = WorkStatus.Pending;
            row.NextAttemptAtUtc = null;
            await reconnect.SaveChangesAsync();
        }
        var plaid = new Mock<IPlaidClient>();
        plaid.Setup(p => p.GetBalancesAsync("access-d", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PlaidAccount("acc-1", "Meridian Plus", null, "7305", "credit", "credit card", "USD", 5_600m, null, 6_500m),
            });
        plaid.Setup(p => p.GetLiabilitiesAsync("access-d", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaidLiability>());
        var processor = new BalanceRefreshProcessor(db, plaid.Object, clock,
            Options.Create(new RefreshOptions { RefreshIntervalHours = 24, MaxAttempts = 5, BaseDelaySeconds = 30, MaxDelaySeconds = 3600 }),
            NullLogger<BalanceRefreshProcessor>.Instance);

        await processor.ProcessNextAsync(CancellationToken.None);

        var stored = await LoadAsync(db, accountId);
        Assert.Equal(5_600m, stored.CurrentBalance);
        Assert.Equal(BalanceSource.Plaid, stored.BalanceSource);
    }
}
