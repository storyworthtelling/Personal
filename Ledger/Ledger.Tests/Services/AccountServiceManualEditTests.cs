using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Services;

/// <summary>
/// Guard R-G15: editing one field of a hand-entered account persists only that field.
///
/// <para>The bug: the post-it's save wrote all five fields on every blur. The Next due picker defaulted to
/// today when no date was stored and the Every control to Month when no cadence was, so editing Balance
/// alone stored "due today" (and a "Due {today}" line on the card) and a cadence the owner never chose;
/// an APR that read as unparseable was written as null, clearing the stored one.</para>
/// </summary>
public class AccountServiceManualEditTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(AccountService Service, Guid Id)> AccountWithAprOnlyAsync(TestDatabase db)
    {
        var created = await new AccountService(db, new FakeClock(Created)).AddManualAccountAsync(
            "Home improvement loan", "Evergreen Home Lending", AccountKind.Loan, 12345.67m, 6.25m, null, null, null, default);
        return (new AccountService(db, new FakeClock(Later)), created.Id);
    }

    [Fact]
    public async Task EditingTheBalanceLeavesTheCadenceAndTheDueDateUnset()
    {
        using var db = new TestDatabase();
        var (service, id) = await AccountWithAprOnlyAsync(db);

        var updated = await service.UpdateManualAccountAsync(id, new ManualAccountPatch { Balance = 41000m }, default);

        Assert.NotNull(updated);
        Assert.Null(updated.PaymentCadence);
        Assert.Null(updated.NextPaymentDueDate);
        Assert.Null(updated.NextPaymentAmount);
        Assert.Equal(6.25m, updated.Apr);
        Assert.Equal(41000m, updated.CurrentBalance);
        Assert.Equal(Later, updated.BalanceAsOfUtc);

        var stored = await service.FindAccountAsync(id, default);
        Assert.Null(stored!.PaymentCadence);
        Assert.Null(stored.NextPaymentDueDate);
        Assert.Null(stored.NextPaymentAmount);
        Assert.Equal(6.25m, stored.Apr);
        Assert.Equal(41000m, stored.CurrentBalance);
    }

    [Fact]
    public async Task EditingTheAprLeavesTheBalanceAndItsUpdatedDateAlone()
    {
        using var db = new TestDatabase();
        var (service, id) = await AccountWithAprOnlyAsync(db);

        var updated = await service.UpdateManualAccountAsync(id, new ManualAccountPatch { Apr = 8.5m }, default);

        Assert.Equal(8.5m, updated!.Apr);
        Assert.Equal(12345.67m, updated.CurrentBalance);
        Assert.Equal(Created, updated.BalanceAsOfUtc);
    }

    [Fact]
    public async Task EachFieldIsWrittenOnItsOwnAndTheOthersSurvive()
    {
        using var db = new TestDatabase();
        var (service, id) = await AccountWithAprOnlyAsync(db);
        var due = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

        await service.UpdateManualAccountAsync(id, new ManualAccountPatch { Payment = 250m }, default);
        await service.UpdateManualAccountAsync(id, new ManualAccountPatch { Cadence = PaymentCadence.Week }, default);
        var updated = await service.UpdateManualAccountAsync(id, new ManualAccountPatch { NextDue = due }, default);

        Assert.Equal(250m, updated!.NextPaymentAmount);
        Assert.Equal(PaymentCadence.Week, updated.PaymentCadence);
        Assert.Equal(due, updated.NextPaymentDueDate);
        Assert.Equal(6.25m, updated.Apr);
        Assert.Equal(12345.67m, updated.CurrentBalance);
    }

    [Fact]
    public async Task AnEmptyPatchWritesNothing()
    {
        using var db = new TestDatabase();
        var (service, id) = await AccountWithAprOnlyAsync(db);

        var updated = await service.UpdateManualAccountAsync(id, new ManualAccountPatch(), default);

        Assert.Equal(Created, updated!.BalanceAsOfUtc);
        Assert.Equal(6.25m, updated.Apr);
        Assert.Null(updated.PaymentCadence);
        Assert.Null(updated.NextPaymentDueDate);
    }

    [Fact]
    public async Task AnAccountThatNoLongerExistsIsReportedAsNull()
    {
        using var db = new TestDatabase();
        var service = new AccountService(db, new FakeClock(Later));

        Assert.Null(await service.UpdateManualAccountAsync(Guid.NewGuid(), new ManualAccountPatch { Balance = 1m }, default));
    }

    // ---- R-G16, the persisted half: an unreadable figure never reaches the patch ----

    [Fact]
    public async Task AnUnreadableAprTypedOverAStoredOneLeavesTheStoredOneInPlace()
    {
        using var db = new TestDatabase();
        var (service, id) = await AccountWithAprOnlyAsync(db);

        // What the page does with the text "abc": it asks the decision, and only a Write becomes a patch.
        var edit = FigureEdit.Decide("abc", FigureKind.Percent, stored: 6.25m);
        if (edit.Outcome == FigureEditOutcome.Write)
        {
            await service.UpdateManualAccountAsync(id, new ManualAccountPatch { Apr = edit.Value }, default);
        }

        Assert.Equal(FigureEditOutcome.Invalid, edit.Outcome);
        Assert.Equal(6.25m, (await service.FindAccountAsync(id, default))!.Apr);
    }
}
