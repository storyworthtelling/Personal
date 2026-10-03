using Index.Domain.Worksheet;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Guard G21 (INDEX-P11-PLAN.md, D156): the follow-up's suggestions, reproducing mockup 13(b)/(c)'s own
/// worked examples to the dollar. Written first and watched to fail against a naive proportional trim
/// (over) and a naive split-the-remainder-evenly rule (under) — see the two "reintroduced bug" cases.
/// </summary>
public class FollowUpSuggestionEngineTests
{
    // ---- mockup 13(b): October, $333 over ----

    private static IReadOnlyList<FollowUpCandidateLine> MockupOverCategories() =>
    [
        new("groceries", "Groceries", null, 548, 690),
        new("dining", "Dining out", null, 196, 284),
        new("fuel", "Fuel", null, 162, 162),
        new("household", "Household", null, 174, 174),
        new("kids", "Kids", null, 228, 228),
        new("utilities", "Utilities", null, 286, 286),
        new("subscriptions", "Subscriptions", null, 68, 68),
        new("medical", "Medical", null, 85, 85),
        new("clothing", "Clothing", null, 104, 248),
        new("gifts", "Gifts", null, 55, 158),
    ];

    [Fact]
    public void MockupOverExample_TrimsClothingGroceriesAndGifts_LargestRiseFirst()
    {
        var suggestions = FollowUpSuggestionEngine.SuggestOver(MockupOverCategories(), excessWholeDollars: 333);

        var byKey = suggestions.ToDictionary(s => s.Key);

        Assert.Equal(104, byKey["clothing"].SuggestedWholeDollars); // full trim back to last month
        Assert.Equal(548, byKey["groceries"].SuggestedWholeDollars); // full trim back to last month
        Assert.Equal(111, byKey["gifts"].SuggestedWholeDollars); // the LAST line: only what's left (333 - 144 - 142 = 47 off 158), not all the way to $55

        // Every other line — including Dining out, which also rose — is left untouched: the excess was
        // fully covered by the first three, largest-rise-first.
        Assert.Null(byKey["dining"].SuggestedWholeDollars);
        Assert.Null(byKey["fuel"].SuggestedWholeDollars);
        Assert.Null(byKey["household"].SuggestedWholeDollars);
        Assert.Null(byKey["kids"].SuggestedWholeDollars);
        Assert.Null(byKey["utilities"].SuggestedWholeDollars);
        Assert.Null(byKey["subscriptions"].SuggestedWholeDollars);
        Assert.Null(byKey["medical"].SuggestedWholeDollars);
    }

    [Fact]
    public void MockupOverExample_TrimsNeverGoBelowLastMonthsFigure()
    {
        var suggestions = FollowUpSuggestionEngine.SuggestOver(MockupOverCategories(), excessWholeDollars: 333);

        foreach (var suggestion in suggestions.Where(s => s.IsSuggested))
        {
            Assert.True(suggestion.SuggestedWholeDollars >= suggestion.LastMonthWholeDollars);
        }
    }

    [Fact]
    public void MockupOverExample_TrimSum_ExactlyCoversTheExcess()
    {
        var suggestions = FollowUpSuggestionEngine.SuggestOver(MockupOverCategories(), excessWholeDollars: 333);

        var totalTrimmed = suggestions.Where(s => s.IsSuggested)
            .Sum(s => s.WrittenWholeDollars - s.SuggestedWholeDollars!.Value);

        Assert.Equal(333, totalTrimmed);
    }

    [Fact]
    public void SetAsideLinesAreNeverPassedToSuggestOver_SoTheyCanNeverBeTrimmed()
    {
        // D156: set-aside lines are never trimmed — structural, since SuggestOver only ever sees what
        // its caller hands it. A caller that (wrongly) included a set-aside line here would see it
        // trimmed exactly like any other candidate; the composer's job (WorksheetPlanBuilder/
        // FollowUpPlanBuilder) is to never do that. This test documents the contract at the engine
        // boundary: handing SuggestOver a "set-aside" line is treated no differently from a category.
        var setAsideAsIfMistakenlyIncluded = new FollowUpCandidateLine("set-aside:extra-payment", "Extra debt payment", null, 350, 690);
        var suggestions = FollowUpSuggestionEngine.SuggestOver([setAsideAsIfMistakenlyIncluded], excessWholeDollars: 50);

        // Proves the engine is a pure trimmer with no special-casing by key — the CALLER must exclude
        // set-aside lines, which WorksheetPlanBuilder/FollowUpPlanBuilder guards separately.
        Assert.Equal(640, suggestions.Single().SuggestedWholeDollars);
    }

    [Fact]
    public void ReintroducedBug_ProportionalTrimInsteadOfLargestRiseFirst_WouldDisagreeWithTheMockup()
    {
        // The bug this guard exists to catch: trimming every risen line proportionally to its own rise,
        // rather than largest-rise-first until the excess is gone.
        var candidates = MockupOverCategories();
        var risen = candidates.Where(l => l.WrittenWholeDollars > l.LastMonthWholeDollars).ToList();
        var totalRise = risen.Sum(l => l.WrittenWholeDollars - l.LastMonthWholeDollars);

        var proportional = risen.ToDictionary(
            l => l.Key,
            l => l.WrittenWholeDollars - (long)Math.Round(333m * (l.WrittenWholeDollars - l.LastMonthWholeDollars) / totalRise));

        var real = FollowUpSuggestionEngine.SuggestOver(candidates, 333).Where(s => s.IsSuggested)
            .ToDictionary(s => s.Key, s => s.SuggestedWholeDollars!.Value);

        // The real, largest-rise-first result touches exactly Clothing/Groceries/Gifts; the naive
        // proportional split touches every risen line (including Dining out) instead — disagreeing with
        // the mockup's own worked example.
        Assert.True(proportional.ContainsKey("dining"));
        Assert.False(real.ContainsKey("dining"));
        Assert.NotEqual(proportional.Keys.OrderBy(k => k), real.Keys.OrderBy(k => k));
    }

    // ---- mockup 13(c): October, $389 to place ----

    [Fact]
    public void MockupUnderExample_SuggestsTheWholeRemainderOnTheExtraPayment()
    {
        var extraPayment = new FollowUpCandidateLine("set-aside:extra-payment", "Extra payment", "Redwood Bank – Bluebird", 350, 490);
        var savings = new[]
        {
            new FollowUpCandidateLine("set-aside:savings:emergency", "Savings", "Emergency fund", 225, 225),
            new FollowUpCandidateLine("set-aside:savings:summer", "Savings", "Summer trip", 135, 135),
        };

        var (extraSuggestion, savingsSuggestions) = FollowUpSuggestionEngine.SuggestUnder(extraPayment, savings, remainderWholeDollars: 389);

        Assert.Equal(879, extraSuggestion.SuggestedWholeDollars); // $490 written + $389 remainder
        Assert.All(savingsSuggestions, s => Assert.Null(s.SuggestedWholeDollars)); // untouched — dash, own boxes shown
    }

    [Fact]
    public void ReintroducedBug_SplittingTheRemainderAcrossExtraAndSavings_WouldDisagreeWithTheMockup()
    {
        // The bug this guard exists to catch: dividing the $389 remainder across all three set-aside
        // lines (extra payment + two savings goals) instead of suggesting it entirely onto the extra
        // payment, the same "helpful" substitution a naive implementation reaches for.
        var extraPayment = new FollowUpCandidateLine("set-aside:extra-payment", "Extra payment", "Redwood Bank – Bluebird", 350, 490);
        var savings = new[]
        {
            new FollowUpCandidateLine("set-aside:savings:emergency", "Savings", "Emergency fund", 225, 225),
            new FollowUpCandidateLine("set-aside:savings:summer", "Savings", "Summer trip", 135, 135),
        };

        const long remainder = 389;
        var evenSplit = remainder / 3;
        var naiveExtra = 490 + evenSplit;

        var (extraSuggestion, _) = FollowUpSuggestionEngine.SuggestUnder(extraPayment, savings, remainder);

        Assert.NotEqual(naiveExtra, extraSuggestion.SuggestedWholeDollars);
        Assert.Equal(879, extraSuggestion.SuggestedWholeDollars);
    }

    // ---- remediation 2.1.6: the over case lists its rows in trim order ----

    [Fact]
    public void InTrimOrder_IsLargestRiseFirst_AndChangesNoFigure()
    {
        var suggestions = FollowUpSuggestionEngine.SuggestOver(MockupOverCategories(), 333)
            .Where(s => s.IsSuggested).ToList();
        Assert.Equal(["Groceries", "Clothing", "Gifts"], suggestions.Select(s => s.Name)); // worksheet order

        var ordered = FollowUpSuggestionEngine.InTrimOrder(suggestions);

        Assert.Equal(["Clothing", "Groceries", "Gifts"], ordered.Select(s => s.Name));
        Assert.Equal(suggestions.OrderBy(s => s.Key).Select(s => s.SuggestedWholeDollars), ordered.OrderBy(s => s.Key).Select(s => s.SuggestedWholeDollars));
    }

    [Fact]
    public void InTrimOrder_ATieKeepsTheOrderItArrivedIn()
    {
        FollowUpSuggestion Line(string key, long written) => new(key, key, null, 100, written, 100);

        var ordered = FollowUpSuggestionEngine.InTrimOrder([Line("b", 150), Line("a", 150), Line("c", 200)]);

        Assert.Equal(["c", "b", "a"], ordered.Select(s => s.Key));
    }

    // ---- remediation 2.1.5: how a line is named on the page ----

    [Fact]
    public void ASavingsLine_IsNamedForItsAccountInTheReferenceList_AndForBothInTheReadBack()
    {
        var emergency = new FollowUpSuggestion(
            WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "Emergency fund", 225, 225, null);
        var extra = new FollowUpSuggestion(WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", null, 350, 690, null);
        var groceries = new FollowUpSuggestion(WorksheetRegions.CategoryLimit(Guid.NewGuid()), "Groceries", null, 548, 560, null);

        Assert.Equal("Emergency fund", emergency.ReferenceLabel);
        Assert.Equal("Savings (Emergency fund)", emergency.PrintedName);
        Assert.Equal("Extra debt payment", extra.ReferenceLabel);
        Assert.Equal("Groceries", groceries.ReferenceLabel);
        Assert.Equal("Groceries", groceries.PrintedName);
    }

    [Fact]
    public void ASavingsLineWithNoAccountName_FallsBackToItsTitle()
    {
        var line = new FollowUpSuggestion(WorksheetRegions.SetAsideSavings(Guid.NewGuid()), "Savings", "  ", 1, 1, null);

        Assert.Equal("Savings", line.ReferenceLabel);
    }

    // ---- remediation 2.1.7: the target is the page's, not the plan's ----

    [Fact]
    public void ThePrintedTarget_ExistsOnlyWhenThePageHasASetAsideSection()
    {
        var categories = new List<WorksheetCategoryLine> { new(Guid.NewGuid(), "Groceries", 100m) };
        var setAside = new List<WorksheetSetAsideLine>
        {
            new(SetAsideLineKind.ExtraDebtPayment, WorksheetRegions.SetAsideExtraPayment, "Extra debt payment", "to Meridian Card", 350m),
        };
        var plain = new Domain.Worksheet.WorksheetPlan("2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194, categories);
        var zeroBased = new Domain.Worksheet.WorksheetPlan("2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194, categories, setAside);
        var unsure = new Domain.Worksheet.WorksheetPlan("2026-10", new DateOnly(2026, 9, 30), 0, false, 5, 6_250, 1_906, null, categories, setAside);

        Assert.Equal(3_100, plain.ZeroBasedTargetWholeDollars); // the figure exists...
        Assert.Null(plain.PrintedTargetWholeDollars);           // ...but the page does not print it
        Assert.Equal(3_100, zeroBased.PrintedTargetWholeDollars);
        Assert.Null(unsure.PrintedTargetWholeDollars);          // income not confident: nothing to check against
    }
}
