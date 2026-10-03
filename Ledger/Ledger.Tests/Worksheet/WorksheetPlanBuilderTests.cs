using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;
using Index.Infrastructure.Worksheet;
using Index.Tests.Support;
using Moq;
using UglyToad.PdfPig;
using Xunit;

namespace Index.Tests.Worksheet;

public class WorksheetPlanBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);

    private static (WorksheetPlanBuilder Builder, BudgetService Budget, TestDatabase Db) Create(
        IncomeEstimate estimate)
    {
        var db = new TestDatabase();
        var budget = new BudgetService(db, new FakeClock(Now), new Mock<IPlaidClient>().Object);
        var snowball = new SnowballService(db, new FakeClock(Now));
        var savings = new SavingsService(db, new FakeClock(Now));
        var income = new Mock<IIncomeDetectionService>();
        income.Setup(i => i.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(estimate);

        var builder = new WorksheetPlanBuilder(budget, income.Object, snowball, savings, new FakeClock(Now));
        return (builder, budget, db);
    }

    [Fact]
    public async Task ConfidentIncome_ProducesALeftToAssignFigure()
    {
        var (builder, budget, db) = Create(new IncomeEstimate(9_350.60m, true, null, []));

        var groceries = await budget.AddCategoryAsync("Groceries", default);
        var september = await budget.GetOrCreateMonthAsync("2026-09", default);
        await budget.EnsureCategoryAmountsAsync(september.Id, false, default);
        await budget.SetCategoryLimitAsync(september.Id, groceries.Id, 600m, default);
        await using (var ctx = db.CreateDbContext())
        {
            var amount = ctx.BudgetCategoryAmounts.Single(a => a.CategoryId == groceries.Id);
            amount.ActualAmount = 548.30m;
            await ctx.SaveChangesAsync();
        }

        var plan = await builder.BuildAsync("2026-10", default);

        Assert.True(plan.IncomeConfident);
        Assert.Equal(9_350, plan.IncomeWholeDollars);
        Assert.Equal(5, plan.FridayCount); // October 2026 has five Fridays
        Assert.Equal(548.30m, plan.Categories.Single(c => c.Name == "Groceries").LastMonthActual);
        Assert.NotNull(plan.LeftToAssignWholeDollars);

        db.Dispose();
    }

    [Fact]
    public async Task NotConfidentIncome_OmitsLeftToAssign_RatherThanPrintingAgainstNothing()
    {
        var (builder, _, db) = Create(IncomeEstimate.NotConfident("irregular deposits"));

        var plan = await builder.BuildAsync("2026-10", default);

        Assert.False(plan.IncomeConfident);
        Assert.Null(plan.LeftToAssignWholeDollars);
        Assert.Equal(0, plan.IncomeWholeDollars);

        db.Dispose();
    }

    [Fact]
    public async Task NoPreviousMonth_LastMonthSpendingIsZero_NotAnException()
    {
        var (builder, _, db) = Create(new IncomeEstimate(5_000m, true, null, []));

        var plan = await builder.BuildAsync("2026-01", default); // nothing recorded for December

        Assert.Equal(0, plan.LastMonthSpendingWholeDollars);

        db.Dispose();
    }

    [Fact]
    public async Task ASnowballPaymentsLineRoundsTheMonthsPaymentsUpAndPrintsThatFigureOnThePage()
    {
        var (builder, _, db) = Create(new IncomeEstimate(9_350.60m, true, null, []));
        await using (var context = db.CreateDbContext())
        {
            var institution = new Institution { Name = "Harbor Credit", AccessToken = string.Empty, CreatedAtUtc = Now.UtcDateTime };
            institution.Accounts.Add(new Account
            {
                Name = "Harbor Plus", Kind = AccountKind.Loan, CurrentBalance = 4_000m, Apr = 0.12m, NextPaymentAmount = 285.42m,
            });
            context.Institutions.Add(institution);
            await context.SaveChangesAsync();
        }

        var plan = await builder.BuildAsync("2026-10", default);

        // 285.42 x 5 Fridays = 1,427.10: the page asks for 1,428, never 1,427 (the old floor) or 1,427 (nearest).
        Assert.Equal(1_428, plan.SnowballPaymentsWholeDollars);
        Assert.Equal(9_350 - 1_428 - plan.LastMonthSpendingWholeDollars, plan.LeftToAssignWholeDollars);

        using var document = PdfDocument.Open(new MemoryStream(WorksheetPainter.Paint(plan)));
        var words = document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).ToList();
        Assert.Contains("\u2212$1,428", words);
        Assert.DoesNotContain("\u2212$1,427", words);

        db.Dispose();
    }
}
