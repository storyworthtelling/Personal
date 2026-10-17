using Constitution.Domain.Scraps;

namespace Constitution.Domain.Scrapbook;

/// <summary>
/// One corner of a cut edge, as the mockup's <c>clip-path: polygon(...)</c> writes it: a fraction
/// of the box plus a fixed number of dp (the CSS <c>calc(100% - 5px)</c> is a fraction of 1 and an
/// offset of -5).
/// </summary>
public readonly record struct CutVertex(double XFraction, double XDp, double YFraction, double YDp)
{
    public ScrapPoint Resolve(double width, double height) =>
        new((XFraction * width) + XDp, (YFraction * height) + YDp);
}

/// <summary>
/// The scissor, torn and serrated edges of the paper kinds (book p.11 to p.16), transcribed from
/// the mockup CSS as data so a painter only walks a list: the clipping's wandering scissor cut
/// (<c>.scrap.mag</c>), the notepad leaf's torn top (<c>.scrap.note</c>), the receipt's serrated
/// foot (<c>.scrap.receipt</c>) and the doctor's chart's uneven torn foot (<c>.scrap.chart</c>).
/// The index card is not cut: it is a flat card with a 3 dp corner.
///
/// <para>A cut applies to the scrap's <i>paper</i> (the box inside its border, see
/// <see cref="ScrapBox.Paper"/>), as the CSS puts the clip on the paper layer and leaves the
/// border element alone.</para>
/// </summary>
public static class ScrapCuts
{
    private static readonly IReadOnlyList<CutVertex> None = [];

    // .scrap.mag: polygon(0 1.5px, 62% 0, 100% 1px, 99.6% 100%, 40% calc(100% - .5px), .4% calc(100% - 1.5px))
    private static readonly IReadOnlyList<CutVertex> Clipping =
    [
        new(0, 0, 0, 1.5),
        new(0.62, 0, 0, 0),
        new(1, 0, 0, 1),
        new(0.996, 0, 1, 0),
        new(0.4, 0, 1, -0.5),
        new(0.004, 0, 1, -1.5),
    ];

    // .scrap.note: a torn top, 0 to 4 px deep, then the two bottom corners.
    private static readonly IReadOnlyList<CutVertex> Leaf = BuildLeaf();

    // .scrap.receipt: the foot is a saw of 5 px teeth, one every 5% of the width.
    private static readonly IReadOnlyList<CutVertex> Slip = BuildSlip();

    // .scrap.chart: a torn foot, teeth 3 to 6 px deep at uneven spacing.
    private static readonly IReadOnlyList<CutVertex> Chart =
    [
        new(0, 0, 0, 0),
        new(1, 0, 0, 0),
        new(1, 0, 1, -6),
        new(0.94, 0, 1, 0),
        new(0.87, 0, 1, -4),
        new(0.79, 0, 1, 0),
        new(0.71, 0, 1, -5),
        new(0.62, 0, 1, 0),
        new(0.55, 0, 1, -3),
        new(0.47, 0, 1, 0),
        new(0.38, 0, 1, -5),
        new(0.30, 0, 1, 0),
        new(0.22, 0, 1, -4),
        new(0.13, 0, 1, 0),
        new(0.06, 0, 1, -6),
        new(0, 0, 1, 0),
    ];

    /// <summary>The corners of the kind's cut, in order, or none when the kind is a plain card.</summary>
    public static IReadOnlyList<CutVertex> For(ScrapKind kind) => kind switch
    {
        ScrapKind.MagazineClipping => Clipping,
        ScrapKind.NotepadLeaf => Leaf,
        ScrapKind.Receipt => Slip,
        ScrapKind.ChartScrap => Chart,
        ScrapKind.IndexCard => None,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A new scrap kind needs its edge read off the mockups."),
    };

    /// <summary>The cut's corners for a paper <paramref name="width"/> by <paramref name="height"/> dp; empty for a plain card.</summary>
    public static IReadOnlyList<ScrapPoint> Resolve(ScrapKind kind, double width, double height) =>
        For(kind).Select(vertex => vertex.Resolve(width, height)).ToList();

    private static IReadOnlyList<CutVertex> BuildLeaf()
    {
        // Pairs of (percent across, px down) for the torn top, as the CSS lists them.
        (double Across, double Down)[] torn =
        [
            (0, 4), (0.04, 0), (0.09, 3), (0.15, 0), (0.22, 4), (0.30, 1), (0.37, 3), (0.45, 0),
            (0.53, 4), (0.61, 1), (0.70, 3), (0.78, 0), (0.86, 4), (0.93, 1), (1, 3),
        ];
        var points = torn.Select(t => new CutVertex(t.Across, 0, 0, t.Down)).ToList();
        points.Add(new CutVertex(1, 0, 1, 0));
        points.Add(new CutVertex(0, 0, 1, 0));
        return points;
    }

    private static IReadOnlyList<CutVertex> BuildSlip()
    {
        var points = new List<CutVertex> { new(0, 0, 0, 0), new(1, 0, 0, 0) };
        // Valleys at 100%, 90%, ... 0% (5 px up from the foot); teeth between them at 95%, 85%, ... 5%.
        for (var step = 0; step < 10; step++)
        {
            points.Add(new CutVertex(1 - (step * 0.1), 0, 1, -5));
            points.Add(new CutVertex(0.95 - (step * 0.1), 0, 1, 0));
        }

        points.Add(new CutVertex(0, 0, 1, -5));
        return points;
    }
}
