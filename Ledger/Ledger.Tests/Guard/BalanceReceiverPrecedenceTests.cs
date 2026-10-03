using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G6 (D87, D91): one incoming Post/<c>BalanceReceiver</c> balance updates exactly one account,
/// even when its merchant name could plausibly match more than one — a hand-entered account's servicer
/// (D87) and a broken Plaid institution of the same name (D91's staleness fallback) never both claim
/// the same receipt.
/// </summary>
public class BalanceReceiverPrecedenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    // ---- the pure precedence (Index.Domain.Model.BalanceReceiverMatch) ----

    [Fact]
    public void Resolve_PicksTheManualAccount_WhenBothAManualAndAStalePlaidAccountShareAName()
    {
        var manualId = Guid.NewGuid();
        var plaidId = Guid.NewGuid();
        var candidates = new[]
        {
            new BalanceMatchCandidate(manualId, "Evergreen Home Lending", BalanceMatchSource.Manual),
            new BalanceMatchCandidate(plaidId, "Evergreen Home Lending", BalanceMatchSource.Plaid),
        };

        var match = BalanceReceiverMatch.Resolve("Evergreen Home Lending", candidates);

        // The reintroduced bug this proves itself against: letting the manual and Plaid candidates
        // both claim it (or picking the Plaid one) instead of the manual account winning (D87 before
        // D91's fallback).
        Assert.Equal(manualId, match);
    }

    [Fact]
    public void Resolve_FallsBackToTheStalePlaidAccount_WhenNoManualAccountMatches()
    {
        var plaidId = Guid.NewGuid();
        var candidates = new[] { new BalanceMatchCandidate(plaidId, "Meridian Card", BalanceMatchSource.Plaid) };

        Assert.Equal(plaidId, BalanceReceiverMatch.Resolve("Meridian Card", candidates));
    }

    [Fact]
    public void Resolve_MatchesCaseInsensitively()
    {
        var manualId = Guid.NewGuid();
        var candidates = new[] { new BalanceMatchCandidate(manualId, "Evergreen Home Lending", BalanceMatchSource.Manual) };

        Assert.Equal(manualId, BalanceReceiverMatch.Resolve("evergreen home lending", candidates));
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNothingMatches()
    {
        var candidates = new[] { new BalanceMatchCandidate(Guid.NewGuid(), "Evergreen Home Lending", BalanceMatchSource.Manual) };

        Assert.Null(BalanceReceiverMatch.Resolve("Redwood Bank", candidates));
    }

    // ---- the end-to-end wiring (AccountService.ApplyBalanceReceiptAsync) ----

    [Fact]
    public async Task ApplyBalanceReceipt_UpdatesTheManualAccount_NotTheBrokenPlaidOne_ForTheSameName()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));

        // A hand-entered "Evergreen Home Lending" loan (AccountService.AddManualAccountAsync's own convention: empty
        // AccessToken/ItemId, RefreshStatus.Completed).
        var manual = await service.AddManualAccountAsync(
            "Car loan", "Evergreen Home Lending", AccountKind.Loan, 12_345.67m,
            apr: null, payment: null, cadence: null, nextDueUtc: null, cancellationToken: default);

        // A SEPARATE, coincidentally-same-named, BROKEN Plaid institution ("Evergreen Home Lending" as a bank, not a
        // lender) — the fallback D91 describes, reached only when nothing manual claims the receipt.
        var plaidInstitution = await service.AddInstitutionAsync("item-gs", "access-gs", "Evergreen Home Lending", default);
        await using (var setup = db.CreateDbContext())
        {
            var institution = await setup.Institutions.FindAsync(plaidInstitution.Id);
            institution!.RefreshStatus = WorkStatus.Failed;
            var plaidAccount = new Account
            {
                InstitutionId = institution.Id,
                AccountId = "plaid-acc-1",
                Name = "Evergreen Home Lending Checking",
                Kind = AccountKind.Depository,
                CurrentBalance = 10m,
            };
            setup.Accounts.Add(plaidAccount);
            await setup.SaveChangesAsync();
        }

        var applied = await service.ApplyBalanceReceiptAsync("Evergreen Home Lending", 11_000.00m, new DateOnly(2026, 9, 20), default);

        Assert.True(applied);
        var institutions = await service.GetInstitutionsAsync(default);
        var manualAccount = institutions.SelectMany(i => i.Accounts).Single(a => a.Id == manual.Id);
        var plaidAccountAfter = institutions.SelectMany(i => i.Accounts).Single(a => a.AccountId == "plaid-acc-1");

        // Exactly one match won: the manual account's balance moved, the Plaid one's did not.
        Assert.Equal(11_000.00m, manualAccount.CurrentBalance);
        Assert.Equal(10m, plaidAccountAfter.CurrentBalance);
    }

    [Fact]
    public async Task ApplyBalanceReceipt_UpdatesAStalePlaidAccount_WhenNoManualAccountMatches()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));

        var institution = await service.AddInstitutionAsync("item-d", "access-d", "Meridian Card", default);
        await using (var setup = db.CreateDbContext())
        {
            var row = await setup.Institutions.FindAsync(institution.Id);
            row!.RefreshStatus = WorkStatus.Failed;
            setup.Accounts.Add(new Account
            {
                InstitutionId = row.Id, AccountId = "disc-1", Name = "Meridian Plus", Kind = AccountKind.Credit,
                CurrentBalance = 5_908.30m,
            });
            await setup.SaveChangesAsync();
        }

        var applied = await service.ApplyBalanceReceiptAsync("Meridian Card", 5_600.00m, asOf: null, default);

        Assert.True(applied);
        var after = (await service.GetInstitutionsAsync(default)).SelectMany(i => i.Accounts).Single(a => a.AccountId == "disc-1");
        Assert.Equal(5_600.00m, after.CurrentBalance);
    }

    // BalanceReceiver itself now never calls ApplyBalanceReceiptAsync when Post/Plate's
    // `sender` extra is absent (it logs an unroutable receipt instead — see its own remarks);
    // this pins that AccountService's matching stays honest even so, in case it is ever
    // reached with nothing to match against: a blank name must not coincidentally match, and
    // must not throw.
    [Fact]
    public async Task ApplyBalanceReceipt_ReturnsFalse_WhenSenderNameIsBlank()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));
        await service.AddManualAccountAsync(
            "Car loan", "Evergreen Home Lending", AccountKind.Loan, 12_345.67m,
            apr: null, payment: null, cadence: null, nextDueUtc: null, cancellationToken: default);

        var applied = await service.ApplyBalanceReceiptAsync("", 1m, asOf: null, default);

        Assert.False(applied);
    }

    [Fact]
    public async Task ApplyBalanceReceipt_NeverUpdatesAStillHealthyPlaidInstitution()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));

        var institution = await service.AddInstitutionAsync("item-h", "access-h", "Redwood Bank", default);
        await using (var setup = db.CreateDbContext())
        {
            setup.Accounts.Add(new Account
            {
                InstitutionId = institution.Id, AccountId = "redwood-1", Name = "Everyday Checking",
                Kind = AccountKind.Depository, CurrentBalance = 3_150.25m,
            });
            await setup.SaveChangesAsync();
        }
        // RefreshStatus stays at its default (Pending) — a healthy connection, not a broken one.

        var applied = await service.ApplyBalanceReceiptAsync("Redwood Bank", 999.00m, asOf: null, default);

        Assert.False(applied);
        var after = (await service.GetInstitutionsAsync(default)).SelectMany(i => i.Accounts).Single(a => a.AccountId == "redwood-1");
        Assert.Equal(3_150.25m, after.CurrentBalance);
    }
}
