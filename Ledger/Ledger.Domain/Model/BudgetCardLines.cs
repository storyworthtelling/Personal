namespace Index.Domain.Model;

/// <summary>One ruled line of a tracking-only card: a label, the figure, and whether it is the quieter
/// comparison line (smaller, soft ink) rather than the figure the card is about.</summary>
public sealed record BudgetCardLine(string Label, decimal Amount, bool Secondary);

/// <summary>
/// What a tracking-only budget card says (D94-D97, D142; mockup 8 frame 2): what has been spent so far,
/// and last month's spend for the same category beneath it "for comparison". No limit, no meter, no
/// "left", no rose. The page turns each line into a row; whether there IS a second line is decided here.
///
/// <para>A category with no recorded last month (the first month it was tracked) gets no comparison line
/// at all — "Last month $0.00" would claim it was measured and came to nothing. A recorded zero is a
/// measurement and is shown.</para>
/// </summary>
public static class BudgetCardLines
{
    public const string SpentLabel = "Spent so far";
    public const string LastMonthLabel = "Last month";

    public static IReadOnlyList<BudgetCardLine> ForTracking(decimal spent, decimal? lastMonth)
    {
        var lines = new List<BudgetCardLine> { new(SpentLabel, spent, Secondary: false) };
        if (lastMonth is { } previous)
        {
            lines.Add(new BudgetCardLine(LastMonthLabel, previous, Secondary: true));
        }
        return lines;
    }
}
