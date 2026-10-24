namespace Constitution.Domain.Budgets;

/// <summary>A month's figures and when this install received them.</summary>
public sealed record BudgetEntry(string Month, decimal Budget, decimal Spent, DateTimeOffset ReceivedAt);

/// <summary>
/// The latest budget and spend Index has told Constitution, one entry per month, oldest month first.
///
/// <para>The wire carries no sequence number, so "latest" is the moment this install received a payload: a payload received
/// strictly later replaces the month's entry, and one received earlier or at the same moment changes nothing. That is what
/// stops a replayed or reordered broadcast from putting last week's spend back over today's.</para>
/// </summary>
public sealed record MonthlyBudgets
{
    private readonly IReadOnlyList<BudgetEntry> entries = [];

    public static MonthlyBudgets None { get; } = new();

    public IReadOnlyList<BudgetEntry> Entries
    {
        get => entries;
        private init => entries = value;
    }

    public BudgetEntry? Find(string month) => entries.FirstOrDefault(entry => entry.Month == month);

    public MonthlyBudgets Apply(BudgetPayload payload, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var held = Find(payload.Month);
        if (held is not null && receivedAt <= held.ReceivedAt)
        {
            return this;
        }

        var fresh = new BudgetEntry(payload.Month, payload.Budget, payload.Spent, receivedAt);
        return From(entries.Where(entry => entry.Month != payload.Month).Append(fresh));
    }

    /// <exception cref="ArgumentException">Two entries name one month.</exception>
    public static MonthlyBudgets From(IEnumerable<BudgetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = entries.OrderBy(entry => entry.Month, StringComparer.Ordinal).ToList();
        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].Month == ordered[index - 1].Month)
            {
                throw new ArgumentException("Two entries name the same month.", nameof(entries));
            }
        }

        return new MonthlyBudgets { Entries = ordered };
    }
}
