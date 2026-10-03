using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Services;

/// <summary>
/// Remediation 3.8: what <c>AddManualAccountAsync</c> stores once Kind has decided the form. The bugs: a
/// hand-entered card had nowhere to keep its limit (so it could never show "of $limit", D125), and a
/// bank account saved an APR, a payment, a cadence and a due date typed before the Kind was switched.
/// </summary>
public class AccountServiceManualAddTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Account> StoredAsync(TestDatabase db, Account added)
    {
        var service = new AccountService(db, new FakeClock(Now));
        return (await service.FindAccountAsync(added.Id, default))!;
    }

    // ---- R-G34: a card's limit is stored ----

    [Fact]
    public async Task ACardKeepsItsLimit()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));

        var added = await service.AddManualAccountAsync(
            "Bluebird Visa", "Redwood Bank", AccountKind.Credit, 4816.25m, 22.49m, 85m, PaymentCadence.Month, Due,
            default, creditLimit: 5000m);

        var stored = await StoredAsync(db, added);
        Assert.Equal(5000m, stored.CreditLimit);
        Assert.Equal(AccountKind.Credit, stored.Kind);
        Assert.Equal(22.49m, stored.Apr);
        Assert.Equal(85m, stored.NextPaymentAmount);
    }

    [Fact]
    public async Task ACardWithNoLimitTypedStoresNone()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));

        var added = await service.AddManualAccountAsync(
            "Bluebird Visa", "Redwood Bank", AccountKind.Credit, 100m, null, null, null, null, default);

        Assert.Null((await StoredAsync(db, added)).CreditLimit);
    }

    [Fact]
    public async Task ALoanNeverStoresALimit()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));

        var added = await service.AddManualAccountAsync(
            "Home improvement loan", "Evergreen Home Lending", AccountKind.Loan, 12345.67m, 6.25m, 250m, PaymentCadence.Month, Due,
            default, creditLimit: 5000m);

        var stored = await StoredAsync(db, added);
        Assert.Null(stored.CreditLimit);
        Assert.Equal(6.25m, stored.Apr);
    }

    // ---- R-G31: a hidden field's value is not saved ----

    [Fact]
    public async Task ABankAccountStoresNoAprPaymentCadenceDueDateOrLimit()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));

        var added = await service.AddManualAccountAsync(
            "Savings", "Redwood Bank", AccountKind.Depository, 8200m, 6.25m, 250m, PaymentCadence.Week, Due,
            default, creditLimit: 5000m);

        var stored = await StoredAsync(db, added);
        Assert.Equal(8200m, stored.CurrentBalance);
        Assert.Null(stored.Apr);
        Assert.Null(stored.NextPaymentAmount);
        Assert.Null(stored.PaymentCadence);
        Assert.Null(stored.NextPaymentDueDate);
        Assert.Null(stored.CreditLimit);
    }

    // ---- the post-it's per-field patch carries the limit ----

    [Fact]
    public async Task EditingACardsLimitChangesOnlyTheLimit()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Now));
        var added = await service.AddManualAccountAsync(
            "Bluebird Visa", "Redwood Bank", AccountKind.Credit, 4816.25m, 22.49m, 85m, PaymentCadence.Month, Due,
            default, creditLimit: 5000m);

        var updated = await service.UpdateManualAccountAsync(
            added.Id, new ManualAccountPatch { CreditLimit = 18000m }, default);

        Assert.Equal(18000m, updated!.CreditLimit);
        Assert.Equal(4816.25m, updated.CurrentBalance);
        Assert.Equal(22.49m, updated.Apr);
        Assert.Equal(85m, updated.NextPaymentAmount);
        Assert.Equal(PaymentCadence.Month, updated.PaymentCadence);
        Assert.Equal(Due, updated.NextPaymentDueDate);
        Assert.Equal(Now, updated.BalanceAsOfUtc); // the balance's "as of" moves only when the balance does
    }
}
