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

/// <summary>
/// D156's own wording: "an empty box takes the suggestion" — a genuinely DIFFERENT read-back rule from
/// the ordinary worksheet's own D151 rule ("a box left empty keeps last month's figure", which in
/// practice means the box's prior persisted value stands, untouched). These tests prove
/// <see cref="FollowUpReadBackService"/> implements the distinct rule, and contrast it directly against
/// <see cref="WorksheetReadBackService"/>'s own opposite behaviour on the same kind of blank input.
/// </summary>
public class FollowUpReadBackServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private static RemarkableDoc Doc() => new() { Id = "fu-1", Title = WorksheetTitles.FollowUpCurrent, PdfBytes = [1, 2, 3] };

    private sealed record Fixture(
        BudgetService Budget, SnowballService Snowball, BudgetMonth Month, Guid CategoryId, Guid SavingsAccountId,
        FakeRemarkableClient Remarkable, FakeHandwritingExtractor Extractor, FollowUpReadBackService Service);

    private static async Task<Fixture> CreateAsync(TestDatabase db)
    {
        var clock = new FakeClock(Now);
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var snowball = new SnowballService(db, clock);

        var category = await budget.AddCategoryAsync("Gifts", default);
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        await budget.SetCategoryLimitAsync(month.Id, category.Id, 158m, default); // "written" before this read-back

        Guid savingsAccountId;
        await using (var context = db.CreateDbContext())
        {
            var institution = new Institution { Name = "Redwood Bank", AccessToken = string.Empty, CreatedAtUtc = Now.UtcDateTime };
            var loan = new Account { Name = "Redwood Bank – Bluebird", Kind = AccountKind.Credit, CurrentBalance = 1_000m, Apr = 0.2m, NextPaymentAmount = 50m };
            var savingsAccount = new Account { Name = "Emergency fund", Kind = AccountKind.Depository, Subtype = "savings" };
            institution.Accounts.Add(loan);
            institution.Accounts.Add(savingsAccount);
            context.Institutions.Add(institution);
            await context.SaveChangesAsync();
            savingsAccountId = savingsAccount.Id;
        }
        await snowball.GetLoansInFocusOrderAsync(default);
        await snowball.SetExtraPaymentAmountAsync(490m, default); // "written" before this read-back
        await budget.EnsureSavingsSetAsideAmountsAsync(month.Id, [(savingsAccountId, 225m)], default);
        await budget.SetSavingsSetAsideLimitAsync(month.Id, savingsAccountId, 225m, default);

        var remarkable = new FakeRemarkableClient { Existing = Doc() };
        var extractor = new FakeHandwritingExtractor();
        Func<string, IHandwritingExtractor> factory = _ => extractor;
        var service = new FollowUpReadBackService(budget, snowball, remarkable, factory);

        return new Fixture(budget, snowball, month, category.Id, savingsAccountId, remarkable, extractor, service);
    }

    private static FollowUpPlan MakePlan(Fixture fx) => new(
        "2026-10", new DateOnly(2026, 10, 2), IsOver: true, DifferenceWholeDollars: 85, TargetWholeDollars: 3_000,
        WrittenTotalWholeDollars: 3_085,
        ActionableLines:
        [
            new FollowUpSuggestion(WorksheetRegions.CategoryLimit(fx.CategoryId), "Gifts", null, 55, 158, 111),
            new FollowUpSuggestion(WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", null, 350, 490, 879),
            new FollowUpSuggestion(WorksheetRegions.SetAsideSavings(fx.SavingsAccountId), "Savings", "Emergency fund", 225, 225, null),
        ],
        ReferenceLines: []);

    [Fact]
    public async Task EveryBoxLeftBlank_TakesEverySuggestion_NotThePriorFigure()
    {
        using var db = new TestDatabase();
        var fx = await CreateAsync(db);
        fx.Extractor.Notes = []; // nothing written anywhere

        await fx.Service.ReadBackAsync(MakePlan(fx), fx.Month.Id, default);

        var amounts = await fx.Budget.GetCategoryAmountsAsync(fx.Month.Id, default);
        Assert.Equal(111m, amounts.Single(a => a.CategoryId == fx.CategoryId).LimitAmount); // the SUGGESTION, not the prior 158

        Assert.Equal(879m, await fx.Snowball.GetExtraPaymentAmountAsync(default)); // the SUGGESTION, not the prior 490

        var savingsAmounts = await fx.Budget.GetSavingsSetAsideAmountsAsync(fx.Month.Id, default);
        Assert.Equal(225m, savingsAmounts.Single().LimitAmount); // no suggestion (dash) — the written figure stands
    }

    [Fact]
    public async Task AWrittenFigure_OverridesThePrintedSuggestion()
    {
        using var db = new TestDatabase();
        var fx = await CreateAsync(db);
        fx.Extractor.Notes = [new ParsedNote { Text = "80", Column = FollowUpRegions.FinalBox(WorksheetRegions.CategoryLimit(fx.CategoryId)), Confidence = 0.95 }];

        await fx.Service.ReadBackAsync(MakePlan(fx), fx.Month.Id, default);

        var amounts = await fx.Budget.GetCategoryAmountsAsync(fx.Month.Id, default);
        Assert.Equal(80m, amounts.Single(a => a.CategoryId == fx.CategoryId).LimitAmount);
    }

    [Fact]
    public async Task LowConfidenceFigure_IsTreatedAsBlank_AndTakesTheSuggestion()
    {
        using var db = new TestDatabase();
        var fx = await CreateAsync(db);
        fx.Extractor.Notes = [new ParsedNote { Text = "??", Column = FollowUpRegions.FinalBox(WorksheetRegions.CategoryLimit(fx.CategoryId)), Confidence = 0.1 }];

        await fx.Service.ReadBackAsync(MakePlan(fx), fx.Month.Id, default);

        var amounts = await fx.Budget.GetCategoryAmountsAsync(fx.Month.Id, default);
        Assert.Equal(111m, amounts.Single(a => a.CategoryId == fx.CategoryId).LimitAmount);
    }

    [Fact]
    public async Task ChangeAnotherLine_MatchesByName_AndAppliesTheFigure()
    {
        using var db = new TestDatabase();
        var fx = await CreateAsync(db);
        fx.Extractor.Notes =
        [
            new ParsedNote { Text = "Gifts", Column = FollowUpRegions.ChangeAnotherName(0), Confidence = 0.9 },
            new ParsedNote { Text = "90", Column = FollowUpRegions.ChangeAnotherAmount(0), Confidence = 0.9 },
        ];

        await fx.Service.ReadBackAsync(MakePlan(fx), fx.Month.Id, default);

        var amounts = await fx.Budget.GetCategoryAmountsAsync(fx.Month.Id, default);
        // The Final box was blank (suggestion 111 would apply), but "change another line" named the SAME
        // line afterward — the change-another write is applied after the final-box pass, so it wins.
        Assert.Equal(90m, amounts.Single(a => a.CategoryId == fx.CategoryId).LimitAmount);
    }

    [Fact]
    public async Task NoDocumentYet_ReturnsNotFound_AndAppliesNothing()
    {
        using var db = new TestDatabase();
        var fx = await CreateAsync(db);
        fx.Remarkable.Existing = null;

        var outcome = await fx.Service.ReadBackAsync(MakePlan(fx), fx.Month.Id, default);

        Assert.True(outcome.Attempted);
        Assert.False(outcome.DocumentFound);
        Assert.Equal(0, outcome.LinesApplied);
    }

    /// <summary>The direct contrast: the SAME kind of blank box, read back by the ORDINARY worksheet's
    /// own service, leaves the prior figure standing — the opposite of the follow-up's own rule proven
    /// above.</summary>
    [Fact]
    public async Task Contrast_TheOrdinaryWorksheetsReadBack_LeavesABlankBoxsPriorFigureStanding()
    {
        using var db = new TestDatabase();
        var clock = new FakeClock(Now);
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var category = await budget.AddCategoryAsync("Gifts", default);
        var month = await budget.GetOrCreateMonthAsync("2026-10", default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        await budget.SetCategoryLimitAsync(month.Id, category.Id, 158m, default); // the prior figure
        await budget.RecordWorksheetDeliveryAsync(month.Id, [category.Id], default);

        var remarkable = new FakeRemarkableClient
        {
            Existing = new RemarkableDoc { Id = "ws-1", Title = WorksheetTitles.Current, PdfBytes = [1, 2, 3] },
        };
        var extractor = new FakeHandwritingExtractor { Notes = [] }; // the box was left blank
        Func<string, IHandwritingExtractor> factory = _ => extractor;
        var service = new WorksheetReadBackService(budget, remarkable, factory);

        await service.ReadBackAsync("2026-10", default);

        var amounts = await budget.GetCategoryAmountsAsync(month.Id, default);
        // Unchanged — the ordinary worksheet's rule leaves the box's prior figure standing on a blank,
        // never substituting anything (there is no "suggestion" concept on that page at all).
        Assert.Equal(158m, amounts.Single(a => a.CategoryId == category.Id).LimitAmount);
    }
}
