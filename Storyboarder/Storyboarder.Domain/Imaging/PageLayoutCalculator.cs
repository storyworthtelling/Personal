using Storyboarder.Domain.Model;

namespace Storyboarder.Domain.Imaging;

public sealed record PageSize(double Width, double Height);

// A panel's placement on the page, in the same units as the PageSize.
public sealed record PanelRect(double X, double Y, double Width, double Height);

// The page's editorial margins. The bottom margin is deeper than the sides so
// the folio band (running title + page number) has a home (see DESIGN.md §7).
public sealed record PageMargins(double Top, double Side, double Bottom);

// Computes a deterministic layout for a page's panels given the page size, margins and gutter. Pure
// math: no rendering. The composer fills these rectangles with the generated panel images.
//
// Two layouts. The count-based overload is the classic uniform grid (stack up to three, two columns
// beyond). The shot-aware overload sizes panels by camera framing: a page of all-Standard shots is
// laid out by that SAME uniform grid — so pages the model didn't tag are unchanged — while a Wide shot
// takes a full-width cinematic band and a Splash fills the page. Panel order (top-to-bottom,
// left-to-right) is preserved, keeping the storyboard's camera progression.
public sealed class PageLayoutCalculator
{
    private readonly PageMargins margins;
    private readonly double gutter;

    public PageLayoutCalculator(PageMargins margins, double gutter)
    {
        if (margins.Top < 0 || margins.Side < 0 || margins.Bottom < 0)
            throw new ArgumentOutOfRangeException(nameof(margins), "Margins cannot be negative.");
        if (gutter < 0)
            throw new ArgumentOutOfRangeException(nameof(gutter), "Gutter cannot be negative.");
        this.margins = margins;
        this.gutter = gutter;
    }

    public IReadOnlyList<PanelRect> Layout(PageSize page, int panelCount)
    {
        if (panelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(panelCount), "A page must have at least one panel.");
        return Uniform(page, panelCount);
    }

    // Shot-aware layout. Falls back to the uniform grid when every panel is a Standard shot, so a page
    // the model didn't tag lays out byte-for-byte as it did before shot types existed.
    public IReadOnlyList<PanelRect> Layout(PageSize page, IReadOnlyList<ShotType> shots)
    {
        if (shots.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(shots), "A page must have at least one panel.");
        if (shots.All(s => s == ShotType.Standard))
            return Uniform(page, shots.Count);
        return Weighted(page, shots);
    }

    // The classic uniform grid: stack up to three panels vertically; two columns beyond that.
    private IReadOnlyList<PanelRect> Uniform(PageSize page, int panelCount)
    {
        int columns = panelCount <= 3 ? 1 : 2;
        int rows = (int)Math.Ceiling(panelCount / (double)columns);

        double usableWidth = page.Width - 2 * margins.Side - (columns - 1) * gutter;
        double usableHeight = page.Height - margins.Top - margins.Bottom - (rows - 1) * gutter;
        if (usableWidth <= 0 || usableHeight <= 0)
            throw new ArgumentException("Page is too small for the requested margins/gutter and panel count.", nameof(page));

        double cellWidth = usableWidth / columns;
        double cellHeight = usableHeight / rows;

        var rects = new List<PanelRect>(panelCount);
        for (int index = 0; index < panelCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            double x = margins.Side + column * (cellWidth + gutter);
            double y = margins.Top + row * (cellHeight + gutter);
            rects.Add(new PanelRect(x, y, cellWidth, cellHeight));
        }
        return rects;
    }

    // Tier-packing by shot: full-width shots (Wide, Splash) take their own row; Standard shots pack two
    // to a row. Row heights are weighted by shot (a splash row is tall, a wide band is cinematic-short),
    // and each row's panels divide its width in proportion to their shot width — so rows always fill the
    // page with no gaps. Camera order is preserved: rows top-to-bottom, panels left-to-right.
    private IReadOnlyList<PanelRect> Weighted(PageSize page, IReadOnlyList<ShotType> shots)
    {
        var tiers = new List<List<int>>();
        var current = new List<int>();
        double currentFraction = 0;
        for (int i = 0; i < shots.Count; i++)
        {
            var fraction = WidthFraction(shots[i]);
            if (OwnTier(shots[i]))
            {
                if (current.Count > 0) { tiers.Add(current); current = []; currentFraction = 0; }
                tiers.Add([i]);
                continue;
            }
            if (current.Count > 0 && currentFraction + fraction > 1.0 + 1e-9)
            {
                tiers.Add(current);
                current = [];
                currentFraction = 0;
            }
            current.Add(i);
            currentFraction += fraction;
        }
        if (current.Count > 0)
            tiers.Add(current);

        double usableHeight = page.Height - margins.Top - margins.Bottom - (tiers.Count - 1) * gutter;
        if (usableHeight <= 0)
            throw new ArgumentException("Page is too small for the requested margins/gutter and panel count.", nameof(page));

        var tierWeights = tiers.Select(tier => tier.Max(i => HeightWeight(shots[i]))).ToList();
        double totalWeight = tierWeights.Sum();

        var rects = new PanelRect[shots.Count];
        double y = margins.Top;
        for (int t = 0; t < tiers.Count; t++)
        {
            var tier = tiers[t];
            double tierHeight = usableHeight * tierWeights[t] / totalWeight;

            double usableWidth = page.Width - 2 * margins.Side - (tier.Count - 1) * gutter;
            if (usableWidth <= 0)
                throw new ArgumentException("Page is too small for the requested margins/gutter and panel count.", nameof(page));
            double fractionSum = tier.Sum(i => WidthFraction(shots[i]));

            double x = margins.Side;
            foreach (var i in tier)
            {
                double width = usableWidth * WidthFraction(shots[i]) / fractionSum;
                rects[i] = new PanelRect(x, y, width, tierHeight);
                x += width + gutter;
            }
            y += tierHeight + gutter;
        }
        return rects;
    }

    // A Wide band and a Splash take the whole row to themselves; a Standard shares (two to a row).
    private static bool OwnTier(ShotType shot) => shot is ShotType.Wide or ShotType.Splash;

    private static double WidthFraction(ShotType shot) => shot switch
    {
        ShotType.Wide => 1.0,
        ShotType.Splash => 1.0,
        _ => 0.5
    };

    private static double HeightWeight(ShotType shot) => shot switch
    {
        ShotType.Splash => 2.0,   // dominates the page
        ShotType.Wide => 0.72,    // cinematic letterbox band
        _ => 1.0
    };
}
