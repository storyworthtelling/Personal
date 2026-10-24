using Constitution.Domain.Budgets;

namespace Constitution.Tests.Budgets;

public class BudgetIntakeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed class MemoryStore : IMonthlyBudgets
    {
        public MonthlyBudgets Held { get; private set; } = MonthlyBudgets.None;

        public Exception? Fails { get; init; }

        public MonthlyBudgets Load() => Held;

        public void Receive(BudgetPayload payload, DateTimeOffset receivedAt)
        {
            if (Fails is not null)
            {
                throw Fails;
            }

            Held = Held.Apply(payload, receivedAt);
        }
    }

    private static Func<string, string?> Extras(string? month, string? budget, string? spent) =>
        key => key switch { "month" => month, "budget" => budget, "spent" => spent, _ => null };

    [Fact]
    public void AGoodBroadcastIsKept()
    {
        var store = new MemoryStore();
        var trail = new List<string>();

        var outcome = new BudgetIntake(store, trail.Add).Receive(Extras("2026-10", "600.00", "412.50"), Noon);

        Assert.Equal(BudgetIntakeOutcome.Kept, outcome);
        Assert.Equal(412.50m, store.Held.Find("2026-10")!.Spent);
        Assert.Single(trail);
    }

    [Fact]
    public void NoAmountOrMonthReachesTheTrail()
    {
        var trail = new List<string>();
        var intake = new BudgetIntake(new MemoryStore(), trail.Add);

        intake.Receive(Extras("2026-10", "600.00", "412.50"), Noon);
        intake.Receive(Extras("2026-10", "600.00", "-412.50"), Noon);
        intake.Receive(Extras("2026-1O", "600.00", "412.50"), Noon);

        Assert.Equal(3, trail.Count);
        Assert.All(trail, line =>
        {
            Assert.DoesNotContain("412", line, StringComparison.Ordinal);
            Assert.DoesNotContain("600", line, StringComparison.Ordinal);
            Assert.DoesNotContain("2026", line, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(null, "600.00", "412.50", "month")]
    [InlineData("2026-10", "six hundred", "412.50", "budget")]
    [InlineData("2026-10", "600.00", "-1", "spent")]
    public void AMalformedBroadcastIsDroppedAndNamedByField(string? month, string? budget, string? spent, string field)
    {
        var store = new MemoryStore();
        var trail = new List<string>();

        var outcome = new BudgetIntake(store, trail.Add).Receive(Extras(month, budget, spent), Noon);

        Assert.Equal(BudgetIntakeOutcome.Dropped, outcome);
        Assert.Empty(store.Held.Entries);
        Assert.Contains(field, Assert.Single(trail), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailingStoreIsNotSwallowed()
    {
        var store = new MemoryStore { Fails = new IOException("disk gone") };

        var thrown = Assert.Throws<IOException>(() =>
            new BudgetIntake(store, _ => { }).Receive(Extras("2026-10", "600.00", "412.50"), Noon));

        Assert.Equal("disk gone", thrown.Message);
    }
}
