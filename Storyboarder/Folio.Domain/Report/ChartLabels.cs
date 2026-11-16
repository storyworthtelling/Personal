namespace Folio.Domain.Report;

/// <summary>A label wanted over a chart point: the point's x and the label's printed width.</summary>
public readonly record struct LabelRequest(double X, double Width);

/// <summary>
/// Where a label went: its left edge, width and height, and the lane it sits in — lane 0 is the row
/// nearest the chart, each further lane one label-height higher.
/// </summary>
public readonly record struct PlacedLabel(double Left, double Width, double Height, int Lane)
{
    public double Right => Left + Width;

    /// <summary>Two labels collide when they share a lane and their spans meet, with a little air.</summary>
    public bool Overlaps(PlacedLabel other) =>
        Lane == other.Lane && Left < other.Right + ChartLabels.Air && other.Left < Right + ChartLabels.Air;
}

/// <summary>
/// Lays out the pacing chart's point labels so no two are printed over one another (R17).
///
/// <para>Labels go in lanes above the chart, each centred over its point (moved in only as far as the
/// chart's edges need), in the lowest lane where it meets no label already placed, and a hairline runs
/// down from it to its point. A crowded chart gets more lanes rather than labels on top of labels or
/// labels dropped: every point keeps its words.</para>
/// </summary>
public static class ChartLabels
{
    /// <summary>The least space between two labels in one lane.</summary>
    public const double Air = 4;

    public static IReadOnlyList<PlacedLabel> Place(
        IReadOnlyList<LabelRequest> requests, double height, double left, double right)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var placed = new List<PlacedLabel>();
        foreach (var request in requests)
        {
            var width = Math.Min(request.Width, right - left);
            var at = Math.Clamp(request.X - (width / 2), left, right - width);
            var lane = 0;
            while (placed.Any(other => new PlacedLabel(at, width, height, lane).Overlaps(other)))
            {
                lane++;
            }

            placed.Add(new PlacedLabel(at, width, height, lane));
        }

        return placed;
    }

    /// <summary>How many lanes a layout used, so the chart can leave room for them.</summary>
    public static int Lanes(IReadOnlyList<PlacedLabel> placed) =>
        placed is { Count: > 0 } ? placed.Max(label => label.Lane) + 1 : 0;
}
