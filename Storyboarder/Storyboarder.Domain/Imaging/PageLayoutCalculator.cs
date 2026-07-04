namespace Storyboarder.Domain.Imaging;

public sealed record PageSize(double Width, double Height);

// A panel's placement on the page, in the same units as the PageSize.
public sealed record PanelRect(double X, double Y, double Width, double Height);

// The page's editorial margins. The bottom margin is deeper than the sides so
// the folio band (running title + page number) has a home (see DESIGN.md §7).
public sealed record PageMargins(double Top, double Side, double Bottom);

// Computes a deterministic grid layout for a page's panels given the page size, margins and gutter.
// Pure math: no rendering. The composer fills these rectangles with the generated panel images.
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

        int columns = ColumnsFor(panelCount);
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

    // Comic-style column choice: stack up to three panels vertically; use two columns beyond that.
    private static int ColumnsFor(int panelCount) => panelCount <= 3 ? 1 : 2;
}
