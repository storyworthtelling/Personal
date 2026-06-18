namespace Storyboarder.Domain.Imaging;

public sealed record PageSize(double Width, double Height);

// A panel's placement on the page, in the same units as the PageSize.
public sealed record PanelRect(double X, double Y, double Width, double Height);

// Computes a deterministic grid layout for a page's panels given the page size, margin and gutter.
// Pure math: no rendering. The composer fills these rectangles with the generated panel images.
public sealed class PageLayoutCalculator
{
    private readonly double margin;
    private readonly double gutter;

    public PageLayoutCalculator(double margin, double gutter)
    {
        if (margin < 0)
            throw new ArgumentOutOfRangeException(nameof(margin), "Margin cannot be negative.");
        if (gutter < 0)
            throw new ArgumentOutOfRangeException(nameof(gutter), "Gutter cannot be negative.");
        this.margin = margin;
        this.gutter = gutter;
    }

    public IReadOnlyList<PanelRect> Layout(PageSize page, int panelCount)
    {
        if (panelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(panelCount), "A page must have at least one panel.");

        int columns = ColumnsFor(panelCount);
        int rows = (int)Math.Ceiling(panelCount / (double)columns);

        double usableWidth = page.Width - 2 * margin - (columns - 1) * gutter;
        double usableHeight = page.Height - 2 * margin - (rows - 1) * gutter;
        if (usableWidth <= 0 || usableHeight <= 0)
            throw new ArgumentException("Page is too small for the requested margin/gutter and panel count.", nameof(page));

        double cellWidth = usableWidth / columns;
        double cellHeight = usableHeight / rows;

        var rects = new List<PanelRect>(panelCount);
        for (int index = 0; index < panelCount; index++)
        {
            int row = index / columns;
            int column = index % columns;
            double x = margin + column * (cellWidth + gutter);
            double y = margin + row * (cellHeight + gutter);
            rects.Add(new PanelRect(x, y, cellWidth, cellHeight));
        }
        return rects;
    }

    // Comic-style column choice: stack up to three panels vertically; use two columns beyond that.
    private static int ColumnsFor(int panelCount) => panelCount <= 3 ? 1 : 2;
}
