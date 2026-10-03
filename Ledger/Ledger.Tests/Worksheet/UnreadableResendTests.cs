using Index.Domain.External;
using Index.Domain.Model;
using Index.Domain.Worksheet;
using Index.Infrastructure.Services;
using Index.Infrastructure.Worksheet;
using Index.Tests.Support;
using Moq;
using Storyworthtelling.Handwriting;
using Storyworthtelling.Remarkable;
using UglyToad.PdfPig;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// R-G25 (INDEX-P11-REMEDIATION-PLAN.md §3.2, §10; D49): "a limit the reader couldn't make out is sent
/// back to the reMarkable". The Budget card has always SAID so ("Sent back to the reMarkable to check"),
/// and the flag was set by the worksheet read-back — but nothing ever sent anything: no follow-up path
/// looked at <c>LimitUnreadable</c>, and a month that balanced was never visited again at all, so the
/// sentence on the card was a promise with no sender behind it.
///
/// <para>These run the real daily cycle (<see cref="WorksheetDeliveryService.RunFollowUpCycleAsync"/>)
/// against the real services and an in-memory database; only the tablet and the handwriting reader are
/// faked. They assert the promise from the outside: a page goes to the tablet, it asks for the figure
/// again, the next morning's read of it clears the flag when the box reads, and a box that does not read
/// (again) is asked again rather than guessed or forgotten.</para>
/// </summary>
public class UnreadableResendTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public required BudgetService Budget { get; init; }
        public required BudgetMonth Month { get; init; }
        public required Guid GroceriesId { get; init; }
        public required Guid DiningId { get; init; }
        public required FakeRemarkableClient Remarkable { get; init; }
        public required FakeHandwritingExtractor Extractor { get; init; }
        public required List<string> Instructions { get; init; }
        public required WorksheetDeliveryService Service { get; init; }
        public required FollowUpPlanBuilder PlanBuilder { get; init; }
        public required SavingsService Savings { get; init; }
        public required TestDatabase Db { get; init; }
    }

    private static readonly RemarkableDoc ThePageOnTheTablet =
        new() { Id = "fu-1", Title = WorksheetTitles.FollowUpCurrent, PdfBytes = [1, 2, 3] };

    /// <summary>October, as the first read-back of the worksheet left it: Groceries read as $100, Dining
    /// out's box unreadable (so its limit is still nothing). <paramref name="settled"/> says how the zero
    /// check came out — true is Approved (it balanced), false is AwaitingConfirmation.</summary>
    private static async Task<Rig> CreateAsync(
        bool settled, long? target, string yearMonth = "2026-10", bool pageAlreadyOut = false, bool diningUnreadable = true)
    {
        var db = new TestDatabase();
        var clock = new FakeClock(Now);
        var budget = new BudgetService(db, clock, new Mock<IPlaidClient>().Object);
        var snowball = new SnowballService(db, clock);
        var savings = new SavingsService(db, clock);

        var groceries = await budget.AddCategoryAsync("Groceries", default);
        var dining = await budget.AddCategoryAsync("Dining out", default);
        var month = await budget.GetOrCreateMonthAsync(yearMonth, default);
        await budget.EnsureCategoryAmountsAsync(month.Id, false, default);
        await budget.SetCategoryLimitAsync(month.Id, groceries.Id, 100m, default);
        if (diningUnreadable)
        {
            await budget.MarkLimitUnreadableAsync(month.Id, dining.Id, default);
        }
        await budget.RecordWorksheetDeliveryAsync(month.Id, [groceries.Id, dining.Id], default, [], target);
        if (settled)
        {
            await budget.MarkBalancedAsync(month.Id, default);
        }
        else
        {
            await budget.MarkAwaitingConfirmationAsync(month.Id, default);
        }

        var remarkable = new FakeRemarkableClient { Existing = pageAlreadyOut ? ThePageOnTheTablet : null };
        var extractor = new FakeHandwritingExtractor();
        var instructions = new List<string>();
        Func<string, IHandwritingExtractor> factory = instruction =>
        {
            instructions.Add(instruction);
            return extractor;
        };

        var planBuilder = new FollowUpPlanBuilder(budget, snowball, savings, clock);
        var service = new WorksheetDeliveryService(
            budget,
            new WorksheetPlanBuilder(budget, new Mock<IIncomeDetectionService>().Object, snowball, savings, clock),
            new WorksheetReadBackService(budget, remarkable, factory, snowball),
            snowball,
            planBuilder,
            new FollowUpReadBackService(budget, snowball, remarkable, factory),
            remarkable,
            clock);

        return new Rig
        {
            Budget = budget, Month = month, GroceriesId = groceries.Id, DiningId = dining.Id,
            Remarkable = remarkable, Extractor = extractor, Instructions = instructions, Service = service, Db = db,
            PlanBuilder = planBuilder, Savings = savings,
        };
    }

    private static async Task<BudgetCategoryAmount> DiningAsync(Rig rig) =>
        (await rig.Budget.GetCategoryAmountsAsync(rig.Month.Id, default)).Single(a => a.CategoryId == rig.DiningId);

    private static string PageText(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return string.Join(" ", document.GetPages().Select(p => p.Text));
    }

    [Fact]
    public async Task ABalancedMonth_WithAnUnreadableLimit_StillSendsAPageBack()
    {
        var fx = await CreateAsync(settled: true, target: 100);
        using var _ = fx.Db;

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.True(result.Ran, "a month that balanced but has a limit nobody could read is still owed a page");
        Assert.True(result.Delivered);
        var upload = Assert.Single(fx.Remarkable.Uploads);
        Assert.Equal(WorksheetTitles.FollowUpCurrent, upload.Title);
        Assert.Contains("Dining out", PageText(upload.PdfBytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheResendPage_HasABoxForTheUnreadableLine_AndTheReadBackIsToldWhereItIs()
    {
        var fx = await CreateAsync(settled: true, target: 100, pageAlreadyOut: true);
        using var _ = fx.Db;

        await fx.Service.RunFollowUpCycleAsync(default);

        // The page that is read the next morning is described to the reader by the same regions the
        // painter draws from (G18): the Final box for exactly this category is among them.
        Assert.Contains(fx.Instructions, i => i.Contains($"final:category:{fx.DiningId}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WritingTheLimitAgain_ClearsTheFlag_AppliesIt_AndStopsTheResend()
    {
        // Unbalanced (Groceries $100 against a $400 target) AND one box unreadable: the page carries both,
        // and the owner's figure in the unreadable line's box is what brings the month to the target.
        var fx = await CreateAsync(settled: false, target: 400, pageAlreadyOut: true);
        using var _ = fx.Db;
        fx.Extractor.Notes = [new ParsedNote { Text = "300", Column = $"final:category:{fx.DiningId}", Confidence = 0.95 }];

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        var dining = await DiningAsync(fx);
        Assert.Equal(300m, dining.LimitAmount);
        Assert.False(dining.LimitUnreadable, "a box that reads clears the flag");
        Assert.True(result.Balanced);
        Assert.False(result.Delivered, "nothing is left to ask, so nothing is sent");
        Assert.Empty(fx.Remarkable.Uploads);
        Assert.Equal(BudgetMonthStatus.Approved, (await fx.Budget.FindMonthAsync("2026-10", default))!.Status);
    }

    [Fact]
    public async Task UnreadableAgain_StaysFlagged_NothingIsGuessed_AndItIsAskedAgain()
    {
        var fx = await CreateAsync(settled: true, target: 100, pageAlreadyOut: true);
        using var _ = fx.Db;
        fx.Extractor.Notes = [new ParsedNote { Text = "??", Column = $"final:category:{fx.DiningId}", Confidence = 0.2 }];

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        var dining = await DiningAsync(fx);
        Assert.True(dining.LimitUnreadable, "still unreadable, so still flagged");
        Assert.Equal(0m, dining.LimitAmount);
        Assert.True(result.Delivered, "and sent again for tomorrow");
        Assert.Single(fx.Remarkable.Uploads);
        Assert.Contains("fu-1", fx.Remarkable.Trashed); // replacing the last page under the same title (D60)
    }

    [Fact]
    public async Task ABoxLeftBlank_IsNotTreatedAsAnAgreement_TheFlagStandsAndItIsAskedAgain()
    {
        // On the follow-up page "an empty box takes the suggestion" (D156) — but an unreadable line has no
        // suggestion, so a blank box must not apply the stored figure and clear the flag with it.
        var fx = await CreateAsync(settled: true, target: 100, pageAlreadyOut: true);
        using var _ = fx.Db;
        fx.Extractor.Notes = [];

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.True((await DiningAsync(fx)).LimitUnreadable);
        Assert.True(result.Delivered);
    }

    [Fact]
    public async Task ALimitThatIsWrittenButBreaksTheBalance_ReturnsTheMonthToTheFollowUpCycle()
    {
        // Approved at $100 with the Dining out box still unread; the owner then writes $300 for it. The
        // month now comes to $400 against a $100 target, so it is no longer settled: it goes back to
        // AwaitingConfirmation and the ordinary over-page goes out.
        var fx = await CreateAsync(settled: true, target: 100, pageAlreadyOut: true);
        using var _ = fx.Db;
        fx.Extractor.Notes = [new ParsedNote { Text = "300", Column = $"final:category:{fx.DiningId}", Confidence = 0.95 }];

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.False((await DiningAsync(fx)).LimitUnreadable);
        Assert.False(result.Balanced);
        Assert.True(result.Delivered);
        Assert.Equal(BudgetMonthStatus.AwaitingConfirmation, (await fx.Budget.FindMonthAsync("2026-10", default))!.Status);
    }

    [Fact]
    public async Task AMonthWithNoPrintedTarget_AndAnUnreadableLimit_IsAskedToo()
    {
        // Income was not confident when the sheet was printed (D103), so there was nothing to balance
        // against and the zero check never runs. The unreadable box is still owed its page.
        var fx = await CreateAsync(settled: false, target: null);
        using var _ = fx.Db;

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.True(result.Ran);
        Assert.True(result.Delivered);
        Assert.Single(fx.Remarkable.Uploads);
    }

    [Fact]
    public async Task AnUnreadableLimitOnAMonthThatIsOver_IsNotPursuedForever()
    {
        var fx = await CreateAsync(settled: true, target: 100, yearMonth: "2026-08");
        using var _ = fx.Db;

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.False(result.Ran);
        Assert.Empty(fx.Remarkable.Uploads);
    }

    [Fact]
    public async Task NothingUnreadable_AndBalanced_SendsNothing()
    {
        var fx = await CreateAsync(settled: true, target: 100, diningUnreadable: false);
        using var _ = fx.Db;

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.False(result.Ran);
        Assert.Empty(fx.Remarkable.Uploads);
    }

    // ---- the plan the page is painted from ----

    [Fact]
    public async Task ThePlanLeadsWithTheUnreadLine_WhichIsNeverASuggestionCandidate_WhateverIsStoredForIt()
    {
        // Dining out has a STALE $500 stored (a previous reading) and is flagged: as a trim candidate it
        // would be the first thing suggested down, on the strength of a figure nobody could read. It is a
        // resend row instead; Groceries is the only line the suggestions are worked out over.
        var fx = await CreateAsync(settled: false, target: 50);
        using var _ = fx.Db;
        await fx.Budget.SetCategoryLimitAsync(fx.Month.Id, fx.DiningId, 500m, default);
        await fx.Budget.MarkLimitUnreadableAsync(fx.Month.Id, fx.DiningId, default);

        var plan = (await fx.PlanBuilder.BuildAsync((await fx.Budget.FindMonthAsync("2026-10", default))!, default))!;

        var first = plan.ActionableLines[0];
        Assert.True(first.WasUnreadable);
        Assert.Equal(WorksheetRegions.CategoryLimit(fx.DiningId), first.Key);
        Assert.Null(first.SuggestedWholeDollars);
        Assert.DoesNotContain(plan.ReferenceLines, l => l.Key == first.Key);
        Assert.DoesNotContain(plan.ActionableLines.Skip(1), l => l.Key == first.Key);
        Assert.True(plan.HasResend);
        Assert.False(plan.IsResendOnly); // it is also over: the ordinary page, with the unread line on it
        Assert.True(plan.IsOver);
    }

    [Fact]
    public async Task ABalancedMonth_WithAnUnreadLine_IsAResendOnlyPage_AndEverythingElseIsReference()
    {
        var fx = await CreateAsync(settled: true, target: 100);
        using var _ = fx.Db;

        var plan = (await fx.PlanBuilder.BuildAsync((await fx.Budget.FindMonthAsync("2026-10", default))!, default))!;

        Assert.True(plan.IsResendOnly);
        Assert.False(plan.IsSettled);
        var line = Assert.Single(plan.ActionableLines);
        Assert.Equal(WorksheetRegions.CategoryLimit(fx.DiningId), line.Key);
        Assert.Equal(WorksheetRegions.CategoryLimit(fx.GroceriesId), Assert.Single(plan.ReferenceLines).Key);
    }

    [Fact]
    public async Task OnceTheBoxReads_ThePlanIsSettled()
    {
        var fx = await CreateAsync(settled: true, target: 100, diningUnreadable: false);
        using var _ = fx.Db;

        var plan = (await fx.PlanBuilder.BuildAsync((await fx.Budget.FindMonthAsync("2026-10", default))!, default))!;

        Assert.True(plan.IsSettled);
        Assert.False(plan.HasResend);
    }

    [Fact]
    public async Task NoTargetAndNothingUnread_HasNoPlanAtAll()
    {
        var fx = await CreateAsync(settled: false, target: null, diningUnreadable: false);
        using var _ = fx.Db;

        Assert.Null(await fx.PlanBuilder.BuildAsync((await fx.Budget.FindMonthAsync("2026-10", default))!, default));
    }

    [Fact]
    public async Task ASavingsSetAsideBoxThatCouldNotBeRead_IsAskedForAgainToo()
    {
        var fx = await CreateAsync(settled: true, target: 100, pageAlreadyOut: true, diningUnreadable: false);
        using var _ = fx.Db;
        Guid savingsId;
        await using (var context = fx.Db.CreateDbContext())
        {
            var institution = new Institution { Name = "Lakeside CU", AccessToken = string.Empty, CreatedAtUtc = Now.UtcDateTime };
            var emergency = new Account { Name = "Emergency fund", Kind = AccountKind.Depository, Subtype = "savings", CurrentBalance = 5_000m };
            institution.Accounts.Add(emergency);
            context.Institutions.Add(institution);
            await context.SaveChangesAsync();
            savingsId = emergency.Id;
        }
        await fx.Budget.EnsureSavingsSetAsideAmountsAsync(fx.Month.Id, [(savingsId, 0m)], default);
        await fx.Budget.MarkSavingsSetAsideLimitUnreadableAsync(fx.Month.Id, savingsId, default);

        var result = await fx.Service.RunFollowUpCycleAsync(default);

        Assert.True(result.Delivered);
        Assert.Contains(fx.Instructions, i => i.Contains($"final:set-aside:savings:{savingsId}", StringComparison.Ordinal));

        // And a figure written in that box reads, applies and clears the flag.
        fx.Extractor.Notes = [new ParsedNote { Text = "250", Column = $"final:set-aside:savings:{savingsId}", Confidence = 0.9 }];
        fx.Instructions.Clear();
        await fx.Service.RunFollowUpCycleAsync(default);
        var row = (await fx.Budget.GetSavingsSetAsideAmountsAsync(fx.Month.Id, default)).Single();
        Assert.Equal(250m, row.LimitAmount);
        Assert.False(row.LimitUnreadable);
    }

    [Fact]
    public async Task TheReadBackIsTold_ForTheUnreadRow_ThatThereIsNoSuggestionToFallBackOn()
    {
        var fx = await CreateAsync(settled: true, target: 100, pageAlreadyOut: true);
        using var _ = fx.Db;

        await fx.Service.RunFollowUpCycleAsync(default);

        var instruction = Assert.Single(fx.Instructions);
        Assert.Contains("NO printed suggestion", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMonthTheCardAndTheDogEarAreDrawnFrom_IsTheOneThePageIsSentFor()
    {
        // The Budget tab names the unread box off BudgetMonthFigures.UnreadableLimits; the page that goes
        // back is built from the same flag. Both must name Dining out and nothing else.
        var fx = await CreateAsync(settled: true, target: 100);
        using var _ = fx.Db;

        var figures = await fx.Budget.GetMonthFiguresAsync(fx.Month, default);
        var plan = (await fx.PlanBuilder.BuildAsync(fx.Month, default))!;

        Assert.Equal(figures.UnreadableLimits.Select(c => c.Name), plan.ResendLines.Select(l => l.Name));
    }
}
