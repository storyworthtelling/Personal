using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;
using Index.Infrastructure.Worksheet;
using Index.Tests.Support;
using Moq;
using Storyworthtelling.Handwriting;

namespace Index.Tests.Worksheet;

/// <summary>
/// Mockup 13's own worked example, built through the real services against an in-memory database so
/// a test can run the whole cycle (print on Sep 30, read back, follow up) and compare with the numbers
/// drawn on the page: $9,350 income, ten loans paying $1,250 a week (Appendix A's D13) for five Fridays
/// = $6,250, September's spending $1,906, a $350 extra debt payment, and two savings goals at $225 and
/// $135 a month. Every figure in the mockup adds up from these, and every one of them is invented.
/// </summary>
internal sealed class WorkedExample : IDisposable
{
    public static readonly (string Name, decimal September)[] Categories =
    [
        ("Groceries", 548m), ("Dining out", 196m), ("Fuel", 162m), ("Household", 174m), ("Kids", 228m),
        ("Utilities", 286m), ("Subscriptions", 68m), ("Medical", 85m), ("Clothing", 104m), ("Gifts", 55m),
    ];

    public TestDatabase Db { get; } = new();
    public FakeClock Clock { get; }
    public BudgetService Budget { get; }
    public SnowballService Snowball { get; }
    public SavingsService Savings { get; }
    public FakeRemarkableClient Remarkable { get; } = new();
    public FakeHandwritingExtractor Extractor { get; } = new();
    public WorksheetDeliveryService Service { get; }
    public FollowUpPlanBuilder FollowUpBuilder { get; }
    public WorksheetPlanBuilder PlanBuilder { get; }
    public Dictionary<string, Guid> CategoryIds { get; } = [];
    public Guid EmergencyId { get; private set; }
    public Guid SummerId { get; private set; }

    private WorkedExample(DateTimeOffset start, IncomeEstimate estimate)
    {
        Clock = new FakeClock(start);
        Budget = new BudgetService(Db, Clock, new Mock<IPlaidClient>().Object);
        Snowball = new SnowballService(Db, Clock);
        Savings = new SavingsService(Db, Clock);
        var income = new Mock<IIncomeDetectionService>();
        income.Setup(i => i.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(estimate);
        PlanBuilder = new WorksheetPlanBuilder(Budget, income.Object, Snowball, Savings, Clock);
        Func<string, IHandwritingExtractor> factory = _ => Extractor;
        var readBack = new WorksheetReadBackService(Budget, Remarkable, factory, Snowball);
        FollowUpBuilder = new FollowUpPlanBuilder(Budget, Snowball, Savings, Clock);
        var followUpReadBack = new FollowUpReadBackService(Budget, Snowball, Remarkable, factory);
        Service = new WorksheetDeliveryService(Budget, PlanBuilder, readBack, Snowball, FollowUpBuilder, followUpReadBack, Remarkable, Clock);
    }

    /// <param name="withLoans">The ten loans of Appendix A's sheet, summed to $1,250 a week.</param>
    /// <param name="withSavingsGoals">Two goals whose recorded history gives a $225 and a $135 monthly pace.</param>
    public static async Task<WorkedExample> CreateAsync(
        DateTimeOffset start, bool withLoans = true, bool withSavingsGoals = true, decimal extraPayment = 350m,
        IncomeEstimate? estimate = null)
    {
        var example = new WorkedExample(start, estimate ?? new IncomeEstimate(9_350m, true, null, []));
        await example.SeedAsync(withLoans, withSavingsGoals, extraPayment);
        return example;
    }

    private async Task SeedAsync(bool withLoans, bool withSavingsGoals, decimal extraPayment)
    {
        var september = await Budget.GetOrCreateMonthAsync("2026-09", default);
        foreach (var (name, _) in Categories)
        {
            CategoryIds[name] = (await Budget.AddCategoryAsync(name, default)).Id;
        }
        await Budget.EnsureCategoryAmountsAsync(september.Id, false, default);
        await using (var context = Db.CreateDbContext())
        {
            foreach (var (name, spent) in Categories)
            {
                context.BudgetCategoryAmounts.Single(a => a.BudgetMonthId == september.Id && a.CategoryId == CategoryIds[name]).ActualAmount = spent;
            }
            await context.SaveChangesAsync();
        }

        if (withLoans)
        {
            // The Meridian card is the focus (first not paid off); the rest is one loan carrying the other
            // $1,150 of Appendix A's $1,250 so the weekly total is the sheet's own.
            await using var context = Db.CreateDbContext();
            var meridian = new Institution { Name = "Meridian Card", AccessToken = string.Empty, CreatedAtUtc = Clock.UtcNow.UtcDateTime };
            meridian.Accounts.Add(new Account { Name = "Meridian Plus", Kind = AccountKind.Loan, CurrentBalance = 7_412.36m, Apr = 0.265m, NextPaymentAmount = 100m });
            var rest = new Institution { Name = "Everything else", AccessToken = string.Empty, CreatedAtUtc = Clock.UtcNow.UtcDateTime };
            rest.Accounts.Add(new Account { Name = "The other nine", Kind = AccountKind.Loan, CurrentBalance = 273_429.75m, Apr = 0.05m, NextPaymentAmount = 1_150m });
            context.Institutions.AddRange(meridian, rest);
            await context.SaveChangesAsync();
            await Snowball.GetLoansInFocusOrderAsync(default); // creates the plan rows
            await Snowball.SetExtraPaymentAmountAsync(extraPayment, default);
        }

        if (withSavingsGoals)
        {
            await using var context = Db.CreateDbContext();
            var bank = new Institution { Name = "Lakeside CU", AccessToken = string.Empty, CreatedAtUtc = Clock.UtcNow.UtcDateTime };
            var emergency = new Account { Name = "Emergency fund", Kind = AccountKind.Depository, Subtype = "savings", CurrentBalance = 5_250m };
            var summer = new Account { Name = "Summer trip", Kind = AccountKind.Depository, Subtype = "savings", CurrentBalance = 1_500m };
            bank.Accounts.Add(emergency);
            bank.Accounts.Add(summer);
            context.Institutions.Add(bank);
            await context.SaveChangesAsync();
            EmergencyId = emergency.Id;
            SummerId = summer.Id;
            // Three months apart: (5,250 - 4,575) / 3 = 225 and (1,500 - 1,095) / 3 = 135 a month.
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = emergency.Id, MonthOf = new DateOnly(2026, 6, 1), Balance = 4_575m });
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = emergency.Id, MonthOf = new DateOnly(2026, 9, 1), Balance = 5_250m });
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = summer.Id, MonthOf = new DateOnly(2026, 6, 1), Balance = 1_095m });
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = summer.Id, MonthOf = new DateOnly(2026, 9, 1), Balance = 1_500m });
            await context.SaveChangesAsync();
            await Savings.SetGoalAsync(emergency.Id, 10_000m, default);
            await Savings.SetGoalAsync(summer.Id, 3_000m, default);
        }
    }

    /// <summary>Runs the cycle on <paramref name="day"/> at 09:00 and leaves the clock there.</summary>
    public async Task<WorksheetDeliveryResult> RunCycleOnAsync(DateOnly day)
    {
        var target = new DateTimeOffset(day.Year, day.Month, day.Day, 9, 0, 0, TimeSpan.Zero);
        Clock.Advance(target - Clock.UtcNow);
        return await Service.RunCycleAsync(default);
    }

    /// <summary>Puts the worksheet the last delivery produced where the next read-back will find it,
    /// exactly as the tablet would hold it.</summary>
    public void TabletNowHoldsLastUpload()
    {
        var last = Remarkable.Uploads[^1];
        Remarkable.Existing = new Storyworthtelling.Remarkable.RemarkableDoc { Id = "doc-" + Remarkable.Uploads.Count, Title = last.Title, PdfBytes = last.PdfBytes };
    }

    public async Task<BudgetMonth> MonthAsync(string yearMonth) => (await Budget.FindMonthAsync(yearMonth, default))!;

    public async Task WriteCategoryLimitsAsync(string yearMonth, params (string Name, decimal Limit)[] limits)
    {
        var month = await MonthAsync(yearMonth);
        await Budget.EnsureCategoryAmountsAsync(month.Id, false, default); // what opening the Budget tab does
        foreach (var (name, limit) in limits)
        {
            await Budget.SetCategoryLimitAsync(month.Id, CategoryIds[name], limit, default);
        }
    }

    public void Dispose() => Db.Dispose();
}
