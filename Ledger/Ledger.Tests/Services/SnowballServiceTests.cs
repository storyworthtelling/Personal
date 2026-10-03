using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Services;

public class SnowballServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static SnowballService Create(TestDatabase db) => new(db, new FakeClock(Now));

    private static async Task<Account> SeedLoanAsync(
        TestDatabase db, string name, decimal balance, decimal apr, decimal weeklyPayment)
    {
        await using var context = db.CreateDbContext();
        var institution = new Institution { Name = name, AccessToken = string.Empty, CreatedAtUtc = Now.UtcDateTime };
        var account = new Account
        {
            Name = name,
            Kind = AccountKind.Loan,
            CurrentBalance = balance,
            Apr = apr,
            NextPaymentAmount = weeklyPayment,
        };
        institution.Accounts.Add(account);
        context.Institutions.Add(institution);
        await context.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task GetLoansInFocusOrder_AutoCreatesAPlanRow_ForEveryEligibleLoan_AtTheBottom()
    {
        using var db = new TestDatabase();
        await SeedLoanAsync(db, "First", 1000m, 0.10m, 100m);
        await SeedLoanAsync(db, "Second", 2000m, 0.10m, 100m);

        var service = Create(db);
        var loans = await service.GetLoansInFocusOrderAsync(default);

        Assert.Equal(["First", "Second"], loans.Select(l => l.Account.Name));
        Assert.Equal(0, loans[0].Plan.FocusOrder);
        Assert.Equal(1, loans[1].Plan.FocusOrder);
    }

    [Fact]
    public async Task LoansWithNoAprOrPayment_AreNotInTheSnowball()
    {
        using var db = new TestDatabase();
        await using (var context = db.CreateDbContext())
        {
            var institution = new Institution { Name = "Bank", AccessToken = string.Empty, CreatedAtUtc = Now.UtcDateTime };
            institution.Accounts.Add(new Account { Name = "Not ready", Kind = AccountKind.Loan, CurrentBalance = 500m });
            context.Institutions.Add(institution);
            await context.SaveChangesAsync();
        }

        var service = Create(db);
        var loans = await service.GetLoansInFocusOrderAsync(default);

        Assert.Empty(loans);
    }

    [Fact]
    public async Task MoveLoanDown_ThenUp_ThenToBottom_ReordersFocusOrder()
    {
        using var db = new TestDatabase();
        var a = await SeedLoanAsync(db, "A", 1000m, 0.10m, 100m);
        var b = await SeedLoanAsync(db, "B", 1000m, 0.10m, 100m);
        var c = await SeedLoanAsync(db, "C", 1000m, 0.10m, 100m);

        var service = Create(db);
        await service.GetLoansInFocusOrderAsync(default); // ensure plans exist: A, B, C

        await service.MoveLoanDownAsync(a.Id, default);
        var afterDown = await service.GetLoansInFocusOrderAsync(default);
        Assert.Equal(["B", "A", "C"], afterDown.Select(l => l.Account.Name));

        await service.MoveLoanUpAsync(a.Id, default);
        var afterUp = await service.GetLoansInFocusOrderAsync(default);
        Assert.Equal(["A", "B", "C"], afterUp.Select(l => l.Account.Name));

        await service.MoveLoanToBottomAsync(a.Id, default);
        var afterBottom = await service.GetLoansInFocusOrderAsync(default);
        Assert.Equal(["B", "C", "A"], afterBottom.Select(l => l.Account.Name));
    }

    [Fact]
    public async Task ExtraPaymentAmount_IsWrittenToEveryPlanRow()
    {
        using var db = new TestDatabase();
        await SeedLoanAsync(db, "A", 1000m, 0.10m, 100m);
        await SeedLoanAsync(db, "B", 1000m, 0.10m, 100m);

        var service = Create(db);
        await service.GetLoansInFocusOrderAsync(default); // ensure plans exist

        await service.SetExtraPaymentAmountAsync(50m, default);

        await using var context = db.CreateDbContext();
        Assert.All(context.SnowballPlans, plan => Assert.Equal(50m, plan.ExtraPaymentAmount));
        Assert.Equal(50m, await service.GetExtraPaymentAmountAsync(default));
    }

    [Fact]
    public async Task ANewLoan_InheritsTheExistingPlansExtraPayment_RatherThanResettingItToZero()
    {
        using var db = new TestDatabase();
        await SeedLoanAsync(db, "A", 1000m, 0.10m, 100m);

        var service = Create(db);
        await service.GetLoansInFocusOrderAsync(default);
        await service.SetExtraPaymentAmountAsync(75m, default);

        await SeedLoanAsync(db, "B", 1000m, 0.10m, 100m);
        var loans = await service.GetLoansInFocusOrderAsync(default);

        var newLoanPlan = loans.Single(l => l.Account.Name == "B").Plan;
        Assert.Equal(75m, newLoanPlan.ExtraPaymentAmount);
    }

    [Fact]
    public async Task GetSummary_PutsPaidOffLoansLast_CrossedOutAndUnnumbered_AndNumbersOnlyTheActiveOnes()
    {
        using var db = new TestDatabase();
        await SeedLoanAsync(db, "PaidOff", 0m, 0.10m, 100m); // AccountStatus.IsPaidOff: Loan/Credit at exactly 0
        await SeedLoanAsync(db, "Active", 5000m, 0.20m, 100m);

        var service = Create(db);
        var summary = await service.GetSummaryAsync(default);

        Assert.Equal(2, summary.LoansInDisplayOrder.Count);
        var active = summary.LoansInDisplayOrder[0];
        var paidOff = summary.LoansInDisplayOrder[1];

        Assert.Equal("Active", active.Name);
        Assert.False(active.IsPaidOff);
        Assert.Equal(1, active.DisplayNumber);
        Assert.True(active.IsFocus);

        Assert.Equal("PaidOff", paidOff.Name);
        Assert.True(paidOff.IsPaidOff);
        Assert.Null(paidOff.DisplayNumber);
        Assert.False(paidOff.IsFocus);
    }

    [Fact]
    public async Task GetSummary_TheCurrentlyFocusedLoansCardShowsTheRolledDownPayment_NotJustItsOwn()
    {
        using var db = new TestDatabase();
        await SeedLoanAsync(db, "First", 1000m, 0.10m, 100m);
        await SeedLoanAsync(db, "Second", 2000m, 0.10m, 50m);

        var service = Create(db);
        var summary = await service.GetSummaryAsync(default);

        var first = summary.LoansInDisplayOrder.Single(l => l.Name == "First");
        var second = summary.LoansInDisplayOrder.Single(l => l.Name == "Second");

        Assert.True(first.IsFocus);
        Assert.Equal(first.FocusedWeeklyPayment, first.WeeklyPaymentNow); // rolled down, per D137
        Assert.False(second.IsFocus);
        Assert.Equal(50m, second.WeeklyPaymentNow); // just its own weekly payment, not yet rolled onto it
    }

    [Fact]
    public async Task RecordCurrentWeekSnapshot_IsDedupedAtWrite_ARerunTheSameWeekAddsNothing()
    {
        using var db = new TestDatabase();
        await SeedLoanAsync(db, "A", 1234.56m, 0.10m, 100m);

        var service = Create(db);
        await service.RecordCurrentWeekSnapshotAsync(default);
        await service.RecordCurrentWeekSnapshotAsync(default);

        await using var context = db.CreateDbContext();
        var snapshot = Assert.Single(context.SnowballSnapshots);
        Assert.Equal(1234.56m, snapshot.Balance);
        Assert.Equal(new DateOnly(2026, 9, 25), snapshot.WeekOf);
    }

    [Fact]
    public async Task GetHistory_ReturnsOneSeriesPerLoan_WithItsOwnPeak()
    {
        using var db = new TestDatabase();
        var loan = await SeedLoanAsync(db, "A", 100m, 0.10m, 10m);

        await using (var context = db.CreateDbContext())
        {
            context.SnowballSnapshots.Add(new SnowballSnapshot { AccountId = loan.Id, WeekOf = new DateOnly(2026, 1, 1), Balance = 500m });
            context.SnowballSnapshots.Add(new SnowballSnapshot { AccountId = loan.Id, WeekOf = new DateOnly(2026, 1, 8), Balance = 300m });
            await context.SaveChangesAsync();
        }

        var service = Create(db);
        var history = await service.GetHistoryAsync(default);

        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8)], history.Weeks);
        var series = Assert.Single(history.LoansInFocusOrder);
        Assert.Equal(500m, series.Peak);
        Assert.Equal(2, series.Points.Count);
    }

    private const string TintedColour = "plaid-primary-colour"; // opaque: PostItTint parses it, not this

    // 1.3c: the post-it is tinted from the bank that owns the loan, so the view has to carry it.
    [Fact]
    public async Task GetSummary_EachLoanCarriesItsBanksPrimaryColour()
    {
        using var db = new TestDatabase();
        var tinted = await SeedLoanAsync(db, "Tinted", 1000m, 0.10m, 100m);
        await SeedLoanAsync(db, "Plain", 2000m, 0.10m, 100m);
        await using (var context = db.CreateDbContext())
        {
            var institution = context.Institutions.Single(i => i.Id == tinted.InstitutionId);
            institution.PrimaryColorHex = TintedColour;
            await context.SaveChangesAsync();
        }

        var summary = await Create(db).GetSummaryAsync(default);

        Assert.Equal(TintedColour, summary.LoansInDisplayOrder.Single(l => l.Name == "Tinted").BankPrimaryColorHex);
        Assert.Null(summary.LoansInDisplayOrder.Single(l => l.Name == "Plain").BankPrimaryColorHex);
    }
}
