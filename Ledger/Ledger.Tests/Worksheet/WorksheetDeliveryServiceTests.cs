using Index.Domain.External;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Infrastructure.Worksheet;
using Index.Tests.Support;
using Moq;
using Storyworthtelling.Handwriting;
using UglyToad.PdfPig;
using Xunit;

namespace Index.Tests.Worksheet;

public class WorksheetDeliveryServiceTests
{
    private static WorksheetDeliveryService Create(
        DateTimeOffset now, TestDatabase db, FakeRemarkableClient remarkable, IncomeEstimate estimate,
        FakeHandwritingExtractor? extractor = null)
    {
        var clock = new FakeClock(now);
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var snowball = new SnowballService(db, clock);
        var savings = new SavingsService(db, clock);
        var income = new Mock<IIncomeDetectionService>();
        income.Setup(i => i.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(estimate);
        var planBuilder = new WorksheetPlanBuilder(budget, income.Object, snowball, savings, clock);

        var fakeExtractor = extractor ?? new FakeHandwritingExtractor();
        Func<string, IHandwritingExtractor> factory = _ => fakeExtractor;
        var readBack = new WorksheetReadBackService(budget, remarkable, factory, snowball);
        var followUpPlanBuilder = new FollowUpPlanBuilder(budget, snowball, savings, clock);
        var followUpReadBack = new FollowUpReadBackService(budget, snowball, remarkable, factory);

        return new WorksheetDeliveryService(budget, planBuilder, readBack, snowball, followUpPlanBuilder, followUpReadBack, remarkable, clock);
    }

    [Fact]
    public async Task NotDueToday_DoesNothing_AndNeverTouchesTheReMarkable()
    {
        using var db = new TestDatabase();
        var remarkable = new FakeRemarkableClient();
        var service = Create(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero), db, remarkable,
            new IncomeEstimate(10_000m, true, null, []));

        var result = await service.RunCycleAsync(default);

        Assert.False(result.Ran);
        Assert.Empty(remarkable.Uploads);
    }

    [Fact]
    public async Task DueToday_DeliversNextMonthsWorksheet_UnderTheFixedTitle_IntoTheIndexFolder()
    {
        using var db = new TestDatabase();
        var remarkable = new FakeRemarkableClient();
        var service = Create(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero), db, remarkable,
            new IncomeEstimate(9_350m, true, null, []));

        var result = await service.RunCycleAsync(default);

        Assert.True(result.Ran);
        Assert.True(result.Delivered);
        Assert.Equal("2026-10", result.TargetYearMonth);

        var upload = Assert.Single(remarkable.Uploads);
        Assert.Equal(WorksheetTitles.Current, upload.Title);
        Assert.Equal("folder:Index", upload.ParentFolderId);
        Assert.True(upload.PdfBytes.Length > 0);
    }

    [Fact]
    public async Task ReplaceByTitle_TrashesWhateverWasThereBeforeUploadingTheNewOne()
    {
        // D60: this month's page replaces last month's — the OLD document (under the same fixed
        // title) is found and trashed before the new one goes up under that same title.
        using var db = new TestDatabase();
        var remarkable = new FakeRemarkableClient
        {
            Existing = new Storyworthtelling.Remarkable.RemarkableDoc
            {
                Id = "old-doc", Title = WorksheetTitles.Current, PdfBytes = [9, 9, 9],
            },
        };
        var service = Create(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero), db, remarkable,
            new IncomeEstimate(9_350m, true, null, []));

        await service.RunCycleAsync(default);

        Assert.Equal(["old-doc"], remarkable.Trashed);
        Assert.Single(remarkable.Uploads); // exactly one worksheet ever on the reMarkable at a time
    }

    [Fact]
    public async Task DeliveryFailure_StillReturnsAResult_AndDoesNotRecordAWorksheetOrder()
    {
        using var db = new TestDatabase();
        var remarkable = new FakeRemarkableClient { ThrowOnUpload = true };
        var service = Create(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero), db, remarkable,
            new IncomeEstimate(9_350m, true, null, []));

        var result = await service.RunCycleAsync(default);

        Assert.True(result.Ran);
        Assert.False(result.Delivered);

        var budget = new BudgetService(db, new FakeClock(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero)), new Mock<IPlaidClient>().Object);
        var month = await budget.FindMonthAsync("2026-10", default);
        // GetOrCreateMonthAsync inside the delivery service still creates the row, but a FAILED
        // delivery must never record a category order for boxes that were never actually printed and
        // sent — RecordWorksheetDeliveryAsync is only called on success.
        Assert.True(month is null || month.WorksheetCategoryOrderJson is null);
    }

    [Fact]
    public async Task DeliveredWorksheet_HasGenuinelyExtractableVectorText_NotARasterPicture()
    {
        // The exact kind of check Storyworthtelling.Paper's own suite already runs for VectorPdfSink —
        // proving the composer produces real vector text, never a bitmap page.
        using var db = new TestDatabase();
        var remarkable = new FakeRemarkableClient();
        var service = Create(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero), db, remarkable,
            new IncomeEstimate(9_350m, true, null, []));

        await service.RunCycleAsync(default);

        var pdfBytes = remarkable.Uploads.Single().PdfBytes;
        using var document = PdfDocument.Open(new MemoryStream(pdfBytes));
        var text = document.GetPage(1).Text;

        Assert.Contains("October 2026", text, StringComparison.Ordinal);
        Assert.Contains("Index", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsBackTheCurrentMonthBeforeComposingTheNextOne()
    {
        // Seed October as already delivered a cycle ago (targeting October, from a September 30 run),
        // with a worksheet waiting on the reMarkable to be read back before November's page goes out.
        using var db = new TestDatabase();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.Zero));
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var groceries = await budget.AddCategoryAsync("Groceries", default);
        var october = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(october.Id, false, default);
        await budget.RecordWorksheetDeliveryAsync(october.Id, [groceries.Id], default);

        var remarkable = new FakeRemarkableClient
        {
            Existing = new Storyworthtelling.Remarkable.RemarkableDoc
            {
                Id = "oct-doc", Title = WorksheetTitles.Current, PdfBytes = [1, 2, 3],
            },
        };
        var extractor = new FakeHandwritingExtractor
        {
            Notes = [new ParsedNote
            {
                Text = "650", Column = Index.Domain.Worksheet.WorksheetRegions.CategoryLimit(groceries.Id), Confidence = 0.9,
            }],
        };

        var income = new Mock<IIncomeDetectionService>();
        income.Setup(i => i.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new IncomeEstimate(10_000m, true, null, []));
        var snowball = new SnowballService(db, clock);
        var savings = new SavingsService(db, clock);
        var planBuilder = new WorksheetPlanBuilder(budget, income.Object, snowball, savings, clock);
        Func<string, IHandwritingExtractor> factory = _ => extractor;
        var readBack = new WorksheetReadBackService(budget, remarkable, factory, snowball);
        var followUpPlanBuilder = new FollowUpPlanBuilder(budget, snowball, savings, clock);
        var followUpReadBack = new FollowUpReadBackService(budget, snowball, remarkable, factory);
        var service = new WorksheetDeliveryService(budget, planBuilder, readBack, snowball, followUpPlanBuilder, followUpReadBack, remarkable, clock);

        var result = await service.RunCycleAsync(default);

        Assert.Equal(1, result.LimitsApplied);
        var amounts = await budget.GetCategoryAmountsAsync(october.Id, default);
        Assert.Equal(650m, amounts.Single(a => a.CategoryId == groceries.Id).LimitAmount);

        // And November's own worksheet still went out.
        Assert.Equal("2026-11", result.TargetYearMonth);
    }
}
