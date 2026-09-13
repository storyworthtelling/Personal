namespace Folio.Domain.Readiness;

/// <summary>
/// What a finding is worth doing something about.
///
/// <para>The first three are the revision plan's own tiers. <see cref="Great"/> is the fourth and it
/// is what makes readiness legible as <i>improvement</i>: Musts falling is only half a picture, and
/// a book with fewer problems is not the same claim as a book with more that is working.</para>
/// </summary>
public enum ReadinessTier
{
    Must,
    Should,
    Consider,
    Great,
}

/// <summary>How many findings of each tier one draft drew.</summary>
public sealed record DraftFindings(int Number, IReadOnlyDictionary<ReadinessTier, int> Counts);

/// <summary>A point on the chart, in the frame's own coordinates.</summary>
public readonly record struct ChartPoint(double X, double Y);

/// <summary>
/// One tier's line across the drafts, and where its name is printed.
///
/// <para>It carries no fill of any kind, deliberately — see <see cref="ReadinessChart"/>.</para>
/// </summary>
public sealed record ReadinessSeries(ReadinessTier Tier, IReadOnlyList<ChartPoint> Points, double LabelY);

/// <summary>The whole plot.</summary>
public sealed record ReadinessPlot(IReadOnlyList<ReadinessSeries> Series);

/// <summary>
/// Readiness across a book's drafts: four counts per draft, plotted as four lines.
///
/// <para><b>Counts, never a score.</b> Nothing here compresses a book into a number — the P1 rule
/// that scoring stays qualitative survives because what is drawn is the findings the report already
/// produced, with Musts falling and Greats rising being what improvement actually looks like.</para>
///
/// <para><b>The form is a line chart because the colour check failed as fills.</b> Run as four
/// adjacent stacked areas, Must and Should measure ΔE 12.6 in normal vision — under the hard floor
/// of 15 — and reordering made it worse. Lines are not adjacent fills: each series is separated by
/// path and position, carries a marker, and is named at its own end. Identity never rests on colour
/// alone, which is the whole point of the check.</para>
/// </summary>
public static class ReadinessChart
{
    /// <summary>Where the first draft sits, and how far apart the drafts are drawn.</summary>
    public const double FirstDraftX = 32;

    public const double DraftSpacingX = 79;

    /// <summary>The rule the counts stand on.</summary>
    public const double BaselineY = 100;

    /// <summary>Air above the tallest count, so the highest line is not welded to the top edge.</summary>
    public const double HeadroomY = 16;

    /// <summary>A line of type needs this much room, or two names print on top of each other.</summary>
    public const double MinimumLabelGap = 13;

    public static ReadinessPlot Plot(IReadOnlyList<DraftFindings> drafts)
    {
        if (drafts.Count == 0)
        {
            return new ReadinessPlot([]);
        }

        var tiers = Enum.GetValues<ReadinessTier>()
            .Where(tier => drafts.Any(draft => draft.Counts.ContainsKey(tier)))
            .ToList();

        // Six pixels a finding is what the frame drew, and it is not a constant: it is what this
        // comes to when the largest count on the chart is 14. A book with worse drafts than that
        // compresses rather than running off the top.
        var largest = drafts.SelectMany(draft => draft.Counts.Values).DefaultIfEmpty(0).Max();
        var scale = largest > 0 ? (BaselineY - HeadroomY) / largest : 0;

        var series = tiers
            .Select(tier => new ReadinessSeries(
                tier,
                [.. drafts.Select((draft, index) => new ChartPoint(
                    FirstDraftX + (index * DraftSpacingX),
                    BaselineY - (Count(draft, tier) * scale)))],
                LabelY: 0))
            .ToList();

        return new ReadinessPlot(PackLabels(series));
    }

    private static int Count(DraftFindings draft, ReadinessTier tier) =>
        draft.Counts.TryGetValue(tier, out var count) ? count : 0;

    /// <summary>
    /// Pushes the end labels apart so no two print over each other, keeping each as near its own
    /// line as it can be. Sorted by where the lines actually end and then separated downward, so a
    /// label never crosses another line's name to reach its own.
    /// </summary>
    private static List<ReadinessSeries> PackLabels(List<ReadinessSeries> series)
    {
        var byEnd = series.OrderBy(one => one.Points[^1].Y).ToList();
        var packed = new List<ReadinessSeries>(byEnd.Count);
        double? previous = null;

        foreach (var one in byEnd)
        {
            var wanted = one.Points[^1].Y;
            var placed = previous is { } last && wanted - last < MinimumLabelGap
                ? last + MinimumLabelGap
                : wanted;

            packed.Add(one with { LabelY = placed });
            previous = placed;
        }

        // Back into tier order: the legend and the drawing both read Must first.
        return [.. packed.OrderBy(one => one.Tier)];
    }
}
