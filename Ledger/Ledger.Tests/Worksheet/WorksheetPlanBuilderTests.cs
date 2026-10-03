using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;
using Index.Infrastructure.Worksheet;
using Index.Tests.Support;
using Moq;
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
}
