using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Infrastructure.Worksheet;
using Index.Tests.Support;
using Moq;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Remarkable;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// D153: the daily follow-up cycle — the SECOND, independent trigger on
/// <see cref="WorksheetDeliveryService.RunCycleAsync"/> (extended in
/// <see cref="WorksheetDeliveryService.RunFollowUpCycleAsync"/>). "Repeats each morning until the
/// figures balance" is proven here across the two outcomes a day can have: a corrected page that now
/// balances (stops), and one that still doesn't (another follow-up goes out for tomorrow).
/// </summary>
public class WorksheetDeliveryServiceFollowUpTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        BudgetService Budget, BudgetMonth Month, Guid GiftsCategoryId, FakeRemarkableClient Remarkable,
        FakeHandwritingExtractor Extractor, WorksheetDeliveryService Service);

    /// <summary>One category ("Gifts", last month $60), written at <paramref name="written"/>, a printed
    /// target of $65 — so a written figure above $65 leaves the month over and unbalanced, exactly the
    /// same shape as mockup 13(b)'s own worked example, just with one line instead of ten.</summary>
    private static async Task<Fixture> CreateAsync(decimal written, RemarkableDoc? existingFollowUp = null, FakeHandwritingExtractor? extractor = null)
    {
        var db = new TestDatabase();
        var clock = new FakeClock(Now);
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var snowball = new SnowballService(db, clock);
        var savings = new SavingsService(db, clock);

        var gifts = await budget.AddCategoryAsync("Gifts", default);
        var october = await budget.GetOrCreateMonthAsync("2026-10", default);
        var september = await budget.GetOrCreateMonthAsync("2026-09", default);
        await budget.EnsureCategoryAmountsAsync(september.Id, false, default);
        await budget.EnsureCategoryAmountsAsync(october.Id, false, default);
        await using (var context = db.CreateDbContext())
        {
            var sep = context.BudgetCategoryAmounts.Single(a => a.BudgetMonthId == september.Id && a.CategoryId == gifts.Id);
            sep.ActualAmount = 60m;
            await context.SaveChangesAsync();
        }
        await budget.SetCategoryLimitAsync(october.Id, gifts.Id, written, default);
        await budget.RecordWorksheetDeliveryAsync(october.Id, [gifts.Id], default, [], printedZeroBasedTargetWholeDollars: 65);
        await budget.MarkAwaitingConfirmationAsync(october.Id, default); // simulate: already read back once, still pending

        var month = (await budget.FindMonthAsync("2026-10", default))!;

        var remarkable = new FakeRemarkableClient { Existing = existingFollowUp };
        var fakeExtractor = extractor ?? new FakeHandwritingExtractor();
        Func<string, IHandwritingExtractor> factory = _ => fakeExtractor;

        var planBuilder = new WorksheetPlanBuilder(budget, new Mock<IIncomeDetectionService>().Object, snowball, savings, clock);
        var readBack = new WorksheetReadBackService(budget, remarkable, factory, snowball);
        var followUpPlanBuilder = new FollowUpPlanBuilder(budget, snowball, savings, clock);
        var followUpReadBack = new FollowUpReadBackService(budget, snowball, remarkable, factory);
        var service = new WorksheetDeliveryService(budget, planBuilder, readBack, snowball, followUpPlanBuilder, followUpReadBack, remarkable, clock);

        return new Fixture(budget, month, gifts.Id, remarkable, fakeExtractor, service);
    }

    [Fact]
    public async Task NoMonthNeedsAFollowUp_ReturnsNotNeeded_AndTouchesNothing()
    {
        using var db = new TestDatabase();
        var clock = new FakeClock(Now);
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var snowball = new SnowballService(db, clock);
        var savings = new SavingsService(db, clock);
        var remarkable = new FakeRemarkableClient();
        var extractor = new FakeHandwritingExtractor();
        Func<string, IHandwritingExtractor> factory = _ => extractor;

        var planBuilder = new WorksheetPlanBuilder(budget, new Mock<IIncomeDetectionService>().Object, snowball, savings, clock);
        var readBack = new WorksheetReadBackService(budget, remarkable, factory, snowball);
        var followUpPlanBuilder = new FollowUpPlanBuilder(budget, snowball, savings, clock);
        var followUpReadBack = new FollowUpReadBackService(budget, snowball, remarkable, factory);
        var service = new WorksheetDeliveryService(budget, planBuilder, readBack, snowball, followUpPlanBuilder, followUpReadBack, remarkable, clock);

        var result = await service.RunFollowUpCycleAsync(default);

        Assert.False(result.Ran);
        Assert.Empty(remarkable.Uploads);
    }

    [Fact]
    public async Task MonthNeedsAFollowUp_NoDocumentYet_DeliversTheFirstOne()
    {
        var fx = await CreateAsync(written: 150m); // over: $150 written against a $65 target

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.True(result.Ran);
        Assert.False(result.Balanced);
        Assert.True(result.Delivered);
        var upload = Assert.Single(fx.Remarkable.Uploads);
        Assert.Equal(WorksheetTitles.FollowUpCurrent, upload.Title);

        var updated = await fx.Budget.FindMonthAsync("2026-10", default);
        Assert.Equal(BudgetMonthStatus.AwaitingConfirmation, updated!.Status); // still pending
    }

    [Fact]
    public async Task FollowUpAlreadyOut_EveryBoxLeftBlank_BalancesAndStops()
    {
        var existingFollowUp = new RemarkableDoc { Id = "fu-1", Title = WorksheetTitles.FollowUpCurrent, PdfBytes = [1, 2, 3] };
        var fx = await CreateAsync(written: 150m, existingFollowUp: existingFollowUp); // extractor.Notes stays empty — every box blank

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.True(result.Ran);
        Assert.True(result.Balanced);
        Assert.False(result.Delivered); // no new follow-up needed

        var amounts = await fx.Budget.GetCategoryAmountsAsync(fx.Month.Id, default);
        Assert.Equal(65m, amounts.Single(a => a.CategoryId == fx.GiftsCategoryId).LimitAmount); // the suggestion was accepted

        var updated = await fx.Budget.FindMonthAsync("2026-10", default);
        Assert.Equal(BudgetMonthStatus.Approved, updated!.Status);
    }

    [Fact]
    public async Task FollowUpAlreadyOut_APartialCorrection_StillDoesNotBalance_SoAnotherGoesOutForTomorrow()
    {
        var existingFollowUp = new RemarkableDoc { Id = "fu-1", Title = WorksheetTitles.FollowUpCurrent, PdfBytes = [1, 2, 3] };
        var extractor = new FakeHandwritingExtractor
        {
            // The owner wrote their own figure — $100 — which is neither the suggestion ($65) nor the
            // target, so the page still doesn't balance.
            Notes = [new ParsedNote { Text = "100", Column = "final:category:PLACEHOLDER", Confidence = 0.9 }],
        };
        var fx = await CreateAsync(written: 150m, existingFollowUp: existingFollowUp, extractor: extractor);
        // The column name needs the real category id — rebuild the note now that we know it.
        extractor.Notes = [new ParsedNote
        {
            Text = "100", Column = $"final:category:{fx.GiftsCategoryId}", Confidence = 0.9,
        }];

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.True(result.Ran);
        Assert.False(result.Balanced);
        Assert.True(result.Delivered); // another follow-up went out

        var amounts = await fx.Budget.GetCategoryAmountsAsync(fx.Month.Id, default);
        Assert.Equal(100m, amounts.Single(a => a.CategoryId == fx.GiftsCategoryId).LimitAmount); // the owner's own figure won

        var updated = await fx.Budget.FindMonthAsync("2026-10", default);
        Assert.Equal(BudgetMonthStatus.AwaitingConfirmation, updated!.Status); // repeats tomorrow (D153)

        // The old follow-up was replaced under the same fixed title (D60's rule, applied here too).
        Assert.Contains("fu-1", fx.Remarkable.Trashed);
        Assert.Single(fx.Remarkable.Uploads);
        Assert.Equal(WorksheetTitles.FollowUpCurrent, fx.Remarkable.Uploads.Single().Title);
    }
}
