using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;
using Index.Infrastructure.Worksheet;
using Index.Tests.Support;
using Moq;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Remarkable;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>D49/D151: a box the reader can't make out is never guessed — it flags
/// <see cref="BudgetCategoryAmount.LimitUnreadable"/> instead, which flows straight into Release E's
/// existing dog-ear wiring (<c>BudgetPage.RefreshNotices</c>) with no change to that page.</summary>
public class WorksheetReadBackServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static (WorksheetReadBackService Service, BudgetService Budget, TestDatabase Db, FakeRemarkableClient Remarkable, FakeHandwritingExtractor Extractor)
        Create()
    {
        var db = new TestDatabase();
        var budget = new BudgetService(db, new FakeClock(Now), new Mock<IPlaidClient>().Object);
        var remarkable = new FakeRemarkableClient();
        var extractor = new FakeHandwritingExtractor();
        Func<string, IHandwritingExtractor> factory = instruction =>
        {
            extractor.RecordInstruction(instruction);
            return extractor;
        };
        var service = new WorksheetReadBackService(budget, remarkable, factory);
        return (service, budget, db, remarkable, extractor);
    }

    private static RemarkableDoc Doc(byte[]? bytes = null) =>
        new() { Id = "doc-1", Title = WorksheetTitles.Current, PdfBytes = bytes ?? [1, 2, 3] };

    [Fact]
    public async Task NeverDelivered_ReturnsNotAttempted_AndNeverCallsTheReMarkable()
    {
        var (service, budget, db, remarkable, _) = Create();
        await budget.GetOrCreateMonthAsync("2026-10", default); // exists, but never delivered as a worksheet

        var outcome = await service.ReadBackAsync("2026-10", default);

        Assert.False(outcome.Attempted);
        Assert.Empty(remarkable.Uploads);
        db.Dispose();
    }

    [Fact]
    public async Task NothingOnTheReMarkable_ReturnsDocumentNotFound()
    {
        var (service, budget, db, remarkable, _) = Create();
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [], default);
        remarkable.Existing = null;

        var outcome = await service.ReadBackAsync("2026-10", default);

        Assert.True(outcome.Attempted);
        Assert.False(outcome.DocumentFound);
        db.Dispose();
    }

    [Fact]
    public async Task ConfidentFigure_SetsTheCategoryLimit()
    {
        var (service, budget, db, remarkable, extractor) = Create();
        var groceries = await budget.AddCategoryAsync("Groceries", default);
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [groceries.Id], default);
        remarkable.Existing = Doc();

        extractor.Notes =
        [
            new ParsedNote { Text = "640", Column = WorksheetRegions.CategoryLimit(groceries.Id), Confidence = 0.95 },
        ];

        var outcome = await service.ReadBackAsync("2026-10", default);

        Assert.Equal(1, outcome.LimitsApplied);
        var amounts = await budget.GetCategoryAmountsAsync(month.Id, default);
        Assert.Equal(640m, amounts.Single(a => a.CategoryId == groceries.Id).LimitAmount);
        Assert.False(amounts.Single(a => a.CategoryId == groceries.Id).LimitUnreadable);

        // The instruction actually built from WorksheetRegions reached the extractor.
        Assert.Single(extractor.InstructionsReceived);
        Assert.Contains(WorksheetRegions.CategoryLimit(groceries.Id), extractor.InstructionsReceived[0]);
        db.Dispose();
    }

    [Fact]
    public async Task LowConfidenceFigure_MarksTheLimitUnreadable_RatherThanGuessing()
    {
        var (service, budget, db, remarkable, extractor) = Create();
        var dining = await budget.AddCategoryAsync("Dining out", default);
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [dining.Id], default);
        remarkable.Existing = Doc();

        extractor.Notes =
        [
            new ParsedNote { Text = "??", Column = WorksheetRegions.CategoryLimit(dining.Id), Confidence = 0.2 },
        ];

        var outcome = await service.ReadBackAsync("2026-10", default);

        Assert.Equal(1, outcome.LimitsMarkedUnreadable);
        var amounts = await budget.GetCategoryAmountsAsync(month.Id, default);
        Assert.True(amounts.Single(a => a.CategoryId == dining.Id).LimitUnreadable);
        Assert.Equal(0m, amounts.Single(a => a.CategoryId == dining.Id).LimitAmount); // never guessed
        db.Dispose();
    }

    [Fact]
    public async Task UnparseableFigure_AlsoMarksUnreadable_EvenAtHighConfidence()
    {
        // A confidently-transcribed word ("illegible", say) is still not a number — never guessed.
        var (service, budget, db, remarkable, extractor) = Create();
        var fuel = await budget.AddCategoryAsync("Fuel", default);
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [fuel.Id], default);
        remarkable.Existing = Doc();

        extractor.Notes =
        [
            new ParsedNote { Text = "scribble", Column = WorksheetRegions.CategoryLimit(fuel.Id), Confidence = 0.9 },
        ];

        var outcome = await service.ReadBackAsync("2026-10", default);

        Assert.Equal(1, outcome.LimitsMarkedUnreadable);
        db.Dispose();
    }

    [Fact]
    public async Task IncomeCorrection_IsAppliedWhenConfident()
    {
        var (service, budget, db, remarkable, extractor) = Create();
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [], default);
        remarkable.Existing = Doc();

        extractor.Notes = [new ParsedNote { Text = "$10,900", Column = WorksheetRegions.IncomeCorrection, Confidence = 0.9 }];

        await service.ReadBackAsync("2026-10", default);

        var updated = await budget.FindMonthAsync("2026-10", default);
        Assert.Equal(10_900m, updated!.IncomeOverride);
        db.Dispose();
    }

    [Fact]
    public async Task NewCategory_WithALegibleNameAndLimit_IsCreatedAndLimited()
    {
        var (service, budget, db, remarkable, extractor) = Create();
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [], default);
        remarkable.Existing = Doc();

        extractor.Notes =
        [
            new ParsedNote { Text = "Pet care", Column = WorksheetRegions.NewCategoryName(0), Confidence = 0.9 },
            new ParsedNote { Text = "75", Column = WorksheetRegions.NewCategoryLimit(0), Confidence = 0.9 },
        ];

        var outcome = await service.ReadBackAsync("2026-10", default);

        Assert.Equal(1, outcome.CategoriesCreated);
        var categories = await budget.GetActiveCategoriesAsync(default);
        var petCare = Assert.Single(categories, c => c.Name == "Pet care");
        var amounts = await budget.GetCategoryAmountsAsync(month.Id, default);
        Assert.Equal(75m, amounts.Single(a => a.CategoryId == petCare.Id).LimitAmount);
        db.Dispose();
    }

    [Fact]
    public async Task NewCategory_NameWrittenButLimitIllegible_IsCreated_ButSentBackToCheck()
    {
        // D151: "a name with no limit, or one it can't read, is sent back to check" — the category is
        // still created (the name was legible), only its limit is marked unreadable.
        var (service, budget, db, remarkable, extractor) = Create();
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [], default);
        remarkable.Existing = Doc();

        extractor.Notes = [new ParsedNote { Text = "Pet care", Column = WorksheetRegions.NewCategoryName(0), Confidence = 0.9 }];

        var outcome = await service.ReadBackAsync("2026-10", default);

        Assert.Equal(1, outcome.CategoriesCreated);
        Assert.Equal(1, outcome.LimitsMarkedUnreadable);
        db.Dispose();
    }

    [Fact]
    public async Task NewCategory_MatchingAnExistingName_UpdatesThatLine_RatherThanDuplicating()
    {
        var (service, budget, db, remarkable, extractor) = Create();
        var groceries = await budget.AddCategoryAsync("Groceries", default);
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [groceries.Id], default);
        remarkable.Existing = Doc();

        // Written on a blank write-in line, but the name matches an existing category exactly
        // (case-insensitively) — D151's own rule.
        extractor.Notes =
        [
            new ParsedNote { Text = "groceries", Column = WorksheetRegions.NewCategoryName(0), Confidence = 0.9 },
            new ParsedNote { Text = "700", Column = WorksheetRegions.NewCategoryLimit(0), Confidence = 0.9 },
        ];

        var outcome = await service.ReadBackAsync("2026-10", default);

        Assert.Equal(0, outcome.CategoriesCreated);
        var categories = await budget.GetActiveCategoriesAsync(default);
        Assert.Single(categories); // no duplicate "groceries" category
        var amounts = await budget.GetCategoryAmountsAsync(month.Id, default);
        Assert.Equal(700m, amounts.Single(a => a.CategoryId == groceries.Id).LimitAmount);
        db.Dispose();
    }

    [Fact]
    public async Task IllegibleNewCategoryName_CreatesNothing()
    {
        var (service, budget, db, remarkable, extractor) = Create();
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [], default);
        remarkable.Existing = Doc();

        extractor.Notes = [new ParsedNote { Text = "squiggle", Column = WorksheetRegions.NewCategoryName(0), Confidence = 0.1 }];

        var outcome = await service.ReadBackAsync("2026-10", default);

        Assert.Equal(0, outcome.CategoriesCreated);
        Assert.Empty(await budget.GetActiveCategoriesAsync(default));
        db.Dispose();
    }

    [Fact]
    public async Task SuccessfulReadBack_MovesTheMonthToAwaitingConfirmation()
    {
        var (service, budget, db, remarkable, _) = Create();
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [], default);
        remarkable.Existing = Doc();

        await service.ReadBackAsync("2026-10", default);

        var updated = await budget.FindMonthAsync("2026-10", default);
        Assert.Equal(BudgetMonthStatus.AwaitingConfirmation, updated!.Status);
        db.Dispose();
    }

    // ---- Release H, Guard G4: the balance check drives the status once a target was printed ----

    [Fact]
    public async Task BalanceCheck_MovesToApproved_WhenTheWrittenLimitExactlyMatchesThePrintedTarget()
    {
        var (service, budget, db, remarkable, extractor) = Create();
        var groceries = await budget.AddCategoryAsync("Groceries", default);
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        // Printed target $640 — this read-back's own written limit will match it exactly.
        await budget.RecordWorksheetDeliveryAsync(month.Id, [groceries.Id], default, printedZeroBasedTargetWholeDollars: 640);
        remarkable.Existing = Doc();

        extractor.Notes = [new ParsedNote { Text = "640", Column = WorksheetRegions.CategoryLimit(groceries.Id), Confidence = 0.9 }];

        await service.ReadBackAsync("2026-10", default);

        var updated = await budget.FindMonthAsync("2026-10", default);
        Assert.Equal(BudgetMonthStatus.Approved, updated!.Status);
        db.Dispose();
    }

    [Fact]
    public async Task BalanceCheck_StaysAwaitingConfirmation_WhenTheWrittenLimitDoesNotMatchTheTarget()
    {
        var (service, budget, db, remarkable, extractor) = Create();
        var groceries = await budget.AddCategoryAsync("Groceries", default);
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [groceries.Id], default, printedZeroBasedTargetWholeDollars: 700);
        remarkable.Existing = Doc();

        extractor.Notes = [new ParsedNote { Text = "640", Column = WorksheetRegions.CategoryLimit(groceries.Id), Confidence = 0.9 }];

        await service.ReadBackAsync("2026-10", default);

        var updated = await budget.FindMonthAsync("2026-10", default);
        Assert.Equal(BudgetMonthStatus.AwaitingConfirmation, updated!.Status); // still pending — the limits themselves were still applied
        var amounts = await budget.GetCategoryAmountsAsync(month.Id, default);
        Assert.Equal(640m, amounts.Single(a => a.CategoryId == groceries.Id).LimitAmount);
        db.Dispose();
    }

    [Fact]
    public async Task NoPrintedTarget_SkipsTheBalanceCheck_KeepingReleaseFsOwnBehaviour()
    {
        // A plan with no confident income (D103) never prints a target — the month must still move
        // forward to AwaitingConfirmation exactly as Release F's own unconditional rule already does.
        var (service, budget, db, remarkable, extractor) = Create();
        var groceries = await budget.AddCategoryAsync("Groceries", default);
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        await budget.RecordWorksheetDeliveryAsync(month.Id, [groceries.Id], default); // no target passed
        remarkable.Existing = Doc();

        extractor.Notes = [new ParsedNote { Text = "1", Column = WorksheetRegions.CategoryLimit(groceries.Id), Confidence = 0.9 }];

        await service.ReadBackAsync("2026-10", default);

        var updated = await budget.FindMonthAsync("2026-10", default);
        Assert.Equal(BudgetMonthStatus.AwaitingConfirmation, updated!.Status);
        db.Dispose();
    }
}
