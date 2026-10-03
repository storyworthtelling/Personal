using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;
using Index.Infrastructure.Worksheet;
using Index.Tests.Support;
using Moq;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Reproduces mockup 13(b)/(c) end to end, through the real <see cref="BudgetService"/>,
/// <see cref="SnowballService"/> and <see cref="SavingsService"/> against an in-memory database — not
/// synthetic inputs handed straight to the pure engine (that's <c>FollowUpSuggestionEngineTests</c>'
/// job), but the whole gathering step a real month's data would go through.
/// </summary>
public class FollowUpPlanBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        BudgetService Budget, SnowballService Snowball, SavingsService Savings, FollowUpPlanBuilder Builder,
        Dictionary<string, Guid> CategoryIds, Guid EmergencyAccountId, Guid SummerAccountId, BudgetMonth October);

    private static async Task<Fixture> SeedAsync(TestDatabase db, (string Name, decimal LastMonth, decimal Written)[] categories)
    {
        var clock = new FakeClock(Now);
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var snowball = new SnowballService(db, clock);
        var savings = new SavingsService(db, clock);

        var categoryIds = new Dictionary<string, Guid>();
        var september = await budget.GetOrCreateMonthAsync("2026-09", default);
        var october = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(september.Id, false, default);

        foreach (var (name, lastMonth, written) in categories)
        {
            var category = await budget.AddCategoryAsync(name, default);
            categoryIds[name] = category.Id;
        }
        await budget.EnsureCategoryAmountsAsync(september.Id, false, default);
        await budget.EnsureCategoryAmountsAsync(october.Id, false, default);

        await using (var context = db.CreateDbContext())
        {
            foreach (var (name, lastMonth, written) in categories)
            {
                var sepAmount = context.BudgetCategoryAmounts.Single(a => a.BudgetMonthId == september.Id && a.CategoryId == categoryIds[name]);
                sepAmount.ActualAmount = lastMonth;
            }
            await context.SaveChangesAsync();
        }
        foreach (var (name, _, written) in categories)
        {
            await budget.SetCategoryLimitAsync(october.Id, categoryIds[name], written, default);
        }

        // The focus loan: "Redwood Bank – Bluebird", last month's extra payment $350.
        await using (var context = db.CreateDbContext())
        {
            var institution = new Institution { Name = "Redwood Bank", AccessToken = string.Empty, CreatedAtUtc = Now.UtcDateTime };
            institution.Accounts.Add(new Account
            {
                Name = "Redwood Bank – Bluebird", Kind = AccountKind.Credit, CurrentBalance = 4_000m, Apr = 0.22m, NextPaymentAmount = 100m,
            });
            context.Institutions.Add(institution);
            await context.SaveChangesAsync();
        }
        await snowball.GetLoansInFocusOrderAsync(default); // auto-creates the SnowballPlan row

        // Two savings goals: Emergency fund, Summer trip.
        Guid emergencyId, summerId;
        await using (var context = db.CreateDbContext())
        {
            var institution = new Institution { Name = "Lakeside CU", AccessToken = string.Empty, CreatedAtUtc = Now.UtcDateTime };
            var emergency = new Account { Name = "Emergency fund", Kind = AccountKind.Depository, Subtype = "savings", CurrentBalance = 5_000m };
            var summer = new Account { Name = "Summer trip", Kind = AccountKind.Depository, Subtype = "savings", CurrentBalance = 1_500m };
            institution.Accounts.Add(emergency);
            institution.Accounts.Add(summer);
            context.Institutions.Add(institution);
            await context.SaveChangesAsync();
            emergencyId = emergency.Id;
            summerId = summer.Id;
        }
        await savings.SetGoalAsync(emergencyId, 10_000m, default);
        await savings.SetGoalAsync(summerId, 3_000m, default);
        await budget.EnsureSavingsSetAsideAmountsAsync(october.Id, [(emergencyId, 225m), (summerId, 135m)], default);

        var builder = new FollowUpPlanBuilder(budget, snowball, savings, clock);
        return new Fixture(budget, snowball, savings, builder, categoryIds, emergencyId, summerId, october);
    }

    private static async Task<BudgetMonth> RecordDeliveryAsync(
        Fixture fx, (string Name, decimal LastMonth, decimal Written)[] categories, long target, long extraPaymentLastMonth)
    {
        var categoryOrder = categories.Select(c => fx.CategoryIds[c.Name]).ToList();
        var setAsideOrder = new List<string>
        {
            WorksheetRegions.SetAsideExtraPayment,
            WorksheetRegions.SetAsideSavings(fx.EmergencyAccountId),
            WorksheetRegions.SetAsideSavings(fx.SummerAccountId),
        };
        await fx.Budget.RecordWorksheetDeliveryAsync(fx.October.Id, categoryOrder, default, setAsideOrder, target, extraPaymentLastMonth);
        return (await fx.Budget.FindMonthAsync("2026-10", default))!;
    }

    private static readonly (string Name, decimal LastMonth, decimal Written)[] OverCaseCategories =
    [
        ("Groceries", 548m, 690m), ("Dining out", 196m, 284m), ("Fuel", 162m, 162m), ("Household", 174m, 174m),
        ("Kids", 228m, 228m), ("Utilities", 286m, 286m), ("Subscriptions", 68m, 68m), ("Medical", 85m, 85m),
        ("Clothing", 104m, 248m), ("Gifts", 55m, 158m),
    ];

    [Fact]
    public async Task MockupOverExample_ProducesExactlyTheThreePrintedSuggestions()
    {
        using var db = new TestDatabase();
        var fx = await SeedAsync(db, OverCaseCategories);
        await fx.Snowball.SetExtraPaymentAmountAsync(690m, default);
        await fx.Budget.SetSavingsSetAsideLimitAsync(fx.October.Id, fx.EmergencyAccountId, 225m, default);
        await fx.Budget.SetSavingsSetAsideLimitAsync(fx.October.Id, fx.SummerAccountId, 135m, default);
        var month = await RecordDeliveryAsync(fx, OverCaseCategories, target: 3_100, extraPaymentLastMonth: 350);

        var plan = await fx.Builder.BuildAsync(month, default);

        Assert.NotNull(plan);
        Assert.True(plan!.IsOver);
        Assert.Equal(333, plan.DifferenceWholeDollars);
        Assert.Equal(3_100, plan.TargetWholeDollars);
        Assert.Equal(3_433, plan.WrittenTotalWholeDollars);

        var actionableByName = plan.ActionableLines.ToDictionary(l => l.Name);
        Assert.Equal(3, plan.ActionableLines.Count);
        Assert.Equal(104, actionableByName["Clothing"].SuggestedWholeDollars);
        Assert.Equal(548, actionableByName["Groceries"].SuggestedWholeDollars);
        Assert.Equal(111, actionableByName["Gifts"].SuggestedWholeDollars);

        // Set-aside lines are never trimmed — they appear only as reference, never actionable.
        Assert.DoesNotContain(plan.ActionableLines, l => l.Name is "Extra debt payment" or "Savings");
        var referenceByName = plan.ReferenceLines.Where(l => l.Name is "Extra debt payment" or "Savings").ToList();
        Assert.Equal(3, referenceByName.Count);
        Assert.Contains(referenceByName, l => l.WrittenWholeDollars == 690); // extra payment
        Assert.Contains(referenceByName, l => l.WrittenWholeDollars == 225); // emergency fund
        Assert.Contains(referenceByName, l => l.WrittenWholeDollars == 135); // summer trip

        Assert.True(plan.WithSuggestionsBalances);
    }

    private static readonly (string Name, decimal LastMonth, decimal Written)[] UnderCaseCategories =
    [
        ("Groceries", 565m, 565m), ("Dining out", 192m, 192m), ("Fuel", 162m, 162m), ("Household", 174m, 174m),
        ("Kids", 222m, 222m), ("Utilities", 286m, 286m), ("Subscriptions", 68m, 68m), ("Medical", 85m, 85m),
        ("Clothing", 64m, 64m), ("Gifts", 43m, 43m),
    ];

    [Fact]
    public async Task MockupUnderExample_SuggestsTheWholeRemainderOnTheExtraPayment()
    {
        using var db = new TestDatabase();
        var fx = await SeedAsync(db, UnderCaseCategories);
        await fx.Snowball.SetExtraPaymentAmountAsync(490m, default);
        await fx.Budget.SetSavingsSetAsideLimitAsync(fx.October.Id, fx.EmergencyAccountId, 225m, default);
        await fx.Budget.SetSavingsSetAsideLimitAsync(fx.October.Id, fx.SummerAccountId, 135m, default);
        var month = await RecordDeliveryAsync(fx, UnderCaseCategories, target: 3_100, extraPaymentLastMonth: 350);

        var plan = await fx.Builder.BuildAsync(month, default);

        Assert.NotNull(plan);
        Assert.False(plan!.IsOver);
        Assert.Equal(389, plan.DifferenceWholeDollars);
        Assert.Equal(2_711, plan.WrittenTotalWholeDollars);

        Assert.Equal(3, plan.ActionableLines.Count); // extra payment + two savings, all boxed
        var extra = plan.ActionableLines.Single(l => l.Name == "Extra debt payment");
        Assert.Equal(879, extra.SuggestedWholeDollars);

        var savingsLines = plan.ActionableLines.Where(l => l.Name == "Savings").ToList();
        Assert.Equal(2, savingsLines.Count);
        Assert.All(savingsLines, l => Assert.Null(l.SuggestedWholeDollars)); // untouched — dash, own boxes

        // Every category is reference-only in the under direction.
        Assert.Equal(10, plan.ReferenceLines.Count(l => l.Name != "Extra debt payment" && l.Name != "Savings"));

        Assert.True(plan.WithSuggestionsBalances);
    }
}
