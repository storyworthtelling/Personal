using System.Globalization;
using Constitution.Domain.Budgets;
using Constitution.Domain.Groceries;

namespace Constitution.Tests.Groceries;

/// <summary>C-6.4.3: Index's grocery budget and spend, read for the sheet. Every figure is invented.</summary>
public class GroceryBudgetTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static MonthlyBudgets Held(string month, string budget, string spent) =>
        MonthlyBudgets.None.Apply(BudgetPayload.Parse(key => key switch { "month" => month, "budget" => budget, _ => spent }).Payload!, Noon);

    [Fact]
    public void NoBudgetNoFlag()
    {
        var weighed = GroceryBudget.Weigh(MonthlyBudgets.None, "2026-10", 9999.99m);

        Assert.Equal(GroceryBudgetState.Unknown, weighed.State);
        Assert.Null(weighed.OverBy);
        Assert.Null(weighed.Remaining);
    }

    [Fact]
    public void AnUnknownBudgetIsNeverZero()
    {
        var status = GroceryBudget.Read(Held("2026-09", "600.00", "100.00"), "2026-10");

        Assert.Equal(GroceryBudgetState.Unknown, status.State);
        Assert.Null(status.Budget);
        Assert.Null(status.Spent);
        Assert.Null(status.Remaining);
        Assert.Null(status.OverBy);
        Assert.Equal("Index has not sent a grocery budget for this month yet.", status.Sentence);
    }

    [Fact]
    public void ABudgetOfNothingIsTakenAsNotSet()
    {
        var status = GroceryBudget.Read(Held("2026-10", "0.00", "40.00"), "2026-10");

        Assert.Equal(GroceryBudgetState.Unknown, status.State);
    }

    [Fact]
    public void WhatIsLeftIsExact()
    {
        var status = GroceryBudget.Read(Held("2026-10", "600.00", "412.50"), "2026-10");

        Assert.Equal(GroceryBudgetState.Within, status.State);
        Assert.Equal(187.50m, status.Remaining);
        Assert.Null(status.OverBy);
        Assert.Equal("187.50 left of this month's 600.00 grocery budget.", status.Sentence);
    }

    [Fact]
    public void WhatIsOverIsExact()
    {
        var status = GroceryBudget.Read(Held("2026-10", "600.00", "650.25"), "2026-10");

        Assert.Equal(GroceryBudgetState.Over, status.State);
        Assert.Equal(50.25m, status.OverBy);
        Assert.Null(status.Remaining);
        Assert.Equal("50.25 over this month's 600.00 grocery budget.", status.Sentence);
    }

    [Fact]
    public void SpendingExactlyTheBudgetIsNotOver()
    {
        var status = GroceryBudget.Read(Held("2026-10", "600.00", "600.00"), "2026-10");

        Assert.Equal(GroceryBudgetState.Within, status.State);
        Assert.Equal(0m, status.Remaining);
    }

    [Fact]
    public void CalibratesToSpend()
    {
        // The week alone fits the budget; with what has already gone this month it does not.
        var budgets = Held("2026-10", "600.00", "520.00");

        var alone = GroceryBudget.Weigh(MonthlyBudgets.None.Apply(new BudgetPayload("2026-10", 600.00m, 0m), Noon), "2026-10", 140.00m);
        var withSpend = GroceryBudget.Weigh(budgets, "2026-10", 140.00m);

        Assert.Equal(GroceryBudgetState.Within, alone.State);
        Assert.Equal(460.00m, alone.Remaining);
        Assert.Equal(GroceryBudgetState.Over, withSpend.State);
        Assert.Equal(60.00m, withSpend.OverBy);
        Assert.Equal("This week's groceries would run 60.00 over this month's 600.00 grocery budget.", withSpend.Sentence);
    }

    [Fact]
    public void AWeekThatFitsSaysWhatWouldBeLeft()
    {
        var weighed = GroceryBudget.Weigh(Held("2026-10", "600.00", "100.00"), "2026-10", 125.50m);

        Assert.Equal(GroceryBudgetState.Within, weighed.State);
        Assert.Equal(374.50m, weighed.Remaining);
        Assert.Equal("This week's groceries fit: 374.50 would be left of this month's 600.00 grocery budget.", weighed.Sentence);
    }

    [Fact]
    public void NoEstimateIsJustTheMonth()
    {
        var weighed = GroceryBudget.Weigh(Held("2026-10", "600.00", "412.50"), "2026-10", null);

        Assert.Equal(GroceryBudget.Read(Held("2026-10", "600.00", "412.50"), "2026-10"), weighed);
    }

    [Fact]
    public void ANegativeEstimateIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GroceryBudget.Weigh(Held("2026-10", "600.00", "1.00"), "2026-10", -1m));
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("October")]
    [InlineData("")]
    public void AMonthIsYearDashMonth(string month)
    {
        Assert.Throws<ArgumentException>(() => GroceryBudget.Read(MonthlyBudgets.None, month));
    }

    [Fact]
    public void TheWeeksMonthIsItsMonday()
    {
        Assert.Equal("2026-10", GroceryBudget.MonthOf(new DateOnly(2026, 10, 26)));
        Assert.Equal("2026-11", GroceryBudget.MonthOf(new DateOnly(2026, 11, 2)));
        Assert.Equal("2027-01", GroceryBudget.MonthOf(new DateOnly(2027, 1, 4)));
    }

    [Fact]
    public void TheWordsFollowNoCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var status = GroceryBudget.Read(Held("2026-10", "1200.50", "412.50"), "2026-10");

            Assert.Equal("788.00 left of this month's 1200.50 grocery budget.", status.Sentence);
            Assert.Equal("2026-10", GroceryBudget.MonthOf(new DateOnly(2026, 10, 26)));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void ToStringShowsNoMoney()
    {
        var text = GroceryBudget.Read(Held("2026-10", "600.00", "412.50"), "2026-10").ToString();

        Assert.DoesNotContain("600", text, StringComparison.Ordinal);
        Assert.DoesNotContain("412", text, StringComparison.Ordinal);
        Assert.DoesNotContain("187", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemainingAndOverAreExactForSeededFigures()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var random = new Random(seed);
            var budget = random.Next(1, 200000) / 100m;
            var spent = random.Next(0, 250000) / 100m;
            var estimate = random.Next(0, 100000) / 100m;
            var budgets = MonthlyBudgets.None.Apply(new BudgetPayload("2026-10", budget, spent), Noon);

            var weighed = GroceryBudget.Weigh(budgets, "2026-10", estimate);

            var projected = spent + estimate;
            if (projected > budget)
            {
                Assert.True(weighed.State == GroceryBudgetState.Over && weighed.OverBy == projected - budget && weighed.Remaining is null, $"seed {seed}");
            }
            else
            {
                Assert.True(weighed.State == GroceryBudgetState.Within && weighed.Remaining == budget - projected && weighed.OverBy is null, $"seed {seed}");
            }
        }
    }
}
