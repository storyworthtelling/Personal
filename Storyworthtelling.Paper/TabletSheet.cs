namespace Storyworthtelling.Paper;

/// <summary>A rectangle in points from the top-left, in whichever space its owner says.</summary>
public readonly record struct SheetRect(float Left, float Top, float Width, float Height);

/// <summary>
/// A document drawn in its own points, placed on the tablet's own page (<see cref="RemarkablePage"/>). Foreword composes
/// in US Letter and Index in A4; the PDF they hand over is 445 × 594 (or 594 × 445 for a landscape drawing), scaled by
/// one factor, so the page is exactly the glass and the type is the size it has always looked on it — the tablet used to
/// do that scaling itself, and letterbox the rest. A region named to a vision read goes through <see cref="ToPage"/>,
/// so the model is told where the ink is on the page it was given, not on the painter's drawing board.
/// </summary>
public readonly record struct TabletSheet(float DesignWidth, float DesignHeight, float Scale, float OffsetX, float OffsetY)
{
    public bool Landscape => DesignWidth > DesignHeight;

    public float PageWidth => Landscape ? RemarkablePage.Height : RemarkablePage.Width;

    public float PageHeight => Landscape ? RemarkablePage.Width : RemarkablePage.Height;

    /// <summary>The drawing as it is, as large as fits, centred on the glass. For a page whose boxes are fixed numbers.</summary>
    public static TabletSheet Fit(float designWidth, float designHeight)
    {
        var (pageWidth, pageHeight) = PageFor(designWidth, designHeight);
        var scale = Math.Min(pageWidth / designWidth, pageHeight / designHeight);
        return new TabletSheet(
            designWidth, designHeight, scale, (pageWidth - designWidth * scale) / 2f, (pageHeight - designHeight * scale) / 2f);
    }

    /// <summary>
    /// The same scale as <see cref="Fit"/>, with the drawing board grown along its short side so it covers the whole
    /// glass. For a layout that flows from its page size, which then gains the room the tablet used to leave blank.
    /// </summary>
    public static TabletSheet Fill(float designWidth, float designHeight)
    {
        var (pageWidth, pageHeight) = PageFor(designWidth, designHeight);
        var scale = Math.Min(pageWidth / designWidth, pageHeight / designHeight);
        return new TabletSheet(pageWidth / scale, pageHeight / scale, scale, 0f, 0f);
    }

    /// <summary>A rectangle on the drawing board, in the page's own points.</summary>
    public SheetRect ToPage(SheetRect design) =>
        new(OffsetX + design.Left * Scale, OffsetY + design.Top * Scale, design.Width * Scale, design.Height * Scale);

    private static (float Width, float Height) PageFor(float designWidth, float designHeight) =>
        designWidth > designHeight
            ? (RemarkablePage.Height, RemarkablePage.Width)
            : (RemarkablePage.Width, RemarkablePage.Height);
}
