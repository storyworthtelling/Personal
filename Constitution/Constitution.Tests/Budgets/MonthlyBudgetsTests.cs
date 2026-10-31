using Constitution.Domain.Budgets;

namespace Constitution.Tests.Budgets;

public class MonthlyBudgetsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static BudgetPayload Payload(string month, string budget, string spent)
    {
        var parsed = BudgetPayload.Parse(key => key switch { "month" => month, "budget" => budget, _ => spent });
        return parsed.Payload!;
    }

    [Fact]
    public void NothingIsKnownAtFirst()
    {
        Assert.Empty(MonthlyBudgets.None.Entries);
        Assert.Null(MonthlyBudgets.None.Find("2026-10"));
    }

    [Fact]
    public void ApplyKeepsTheMonth()
    {
        var kept = MonthlyBudgets.None.Apply(Payload("2026-10", "600.00", "412.50"), Noon);

        var entry = Assert.Single(kept.Entries);
        Assert.Equal(new BudgetEntry("2026-10", 600.00m, 412.50m, Noon), entry);
        Assert.Same(entry, kept.Find("2026-10"));
    }

    [Fact]
    public void ANewerPayloadReplacesTheMonth()
    {
        var first = MonthlyBudgets.None.Apply(Payload("2026-10", "600.00", "100.00"), Noon);
        var second = first.Apply(Payload("2026-10", "650.00", "412.50"), Noon.AddMinutes(1));

        var entry = Assert.Single(second.Entries);
        Assert.Equal(650.00m, entry.Budget);
        Assert.Equal(412.50m, entry.Spent);
        Assert.Equal(Noon.AddMinutes(1), entry.ReceivedAt);
    }

    [Fact]
    public void AnOlderPayloadNeverRegressesTheMonth()
    {
        var first = MonthlyBudgets.None.Apply(Payload("2026-10", "650.00", "412.50"), Noon);
        var second = first.Apply(Payload("2026-10", "600.00", "100.00"), Noon.AddMinutes(-5));

        Assert.Equal(first, second);
        Assert.Equal(412.50m, second.Find("2026-10")!.Spent);
    }

    [Fact]
    public void AnEqualMomentNeverReplacesEither()
    {
        var first = MonthlyBudgets.None.Apply(Payload("2026-10", "650.00", "412.50"), Noon);
        var second = first.Apply(Payload("2026-10", "600.00", "100.00"), Noon);

        Assert.Equal(first, second);
    }

    [Fact]
    public void MonthsAreKeptApartAndInOrder()
    {
        var kept = MonthlyBudgets.None
            .Apply(Payload("2026-10", "600.00", "412.50"), Noon)
            .Apply(Payload("2026-09", "590.00", "580.00"), Noon.AddMinutes(-90))
            .Apply(Payload("2026-11", "610.00", "0.00"), Noon.AddMinutes(1));

        Assert.Equal(["2026-09", "2026-10", "2026-11"], kept.Entries.Select(entry => entry.Month));
        Assert.Equal(580.00m, kept.Find("2026-09")!.Spent);
    }

    [Fact]
    public void AnOldMonthIsNotRegressedByANewerOtherMonth()
    {
        var kept = MonthlyBudgets.None
            .Apply(Payload("2026-10", "600.00", "412.50"), Noon)
            .Apply(Payload("2026-09", "590.00", "580.00"), Noon.AddMinutes(-90));

        Assert.Equal(412.50m, kept.Find("2026-10")!.Spent);
    }

    [Fact]
    public void FromRefusesTwoEntriesForOneMonth()
    {
        Assert.Throws<ArgumentException>(() => MonthlyBudgets.From(
        [
            new BudgetEntry("2026-10", 1m, 1m, Noon),
            new BudgetEntry("2026-10", 2m, 2m, Noon.AddMinutes(1)),
        ]));
    }
}
