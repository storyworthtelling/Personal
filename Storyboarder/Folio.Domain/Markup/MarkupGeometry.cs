using Folio.Domain.Report;

namespace Folio.Domain.Markup;

/// <summary>
/// The marked-up manuscript page, in points, read off the drawn page (mockup p. 61).
///
/// <para>The content box is the report's own — 489 × 732 — and it is divided rather than added to:
/// the manuscript column, a gutter, and the margin the notes live in tile it exactly, so the marked
/// page is the same sheet as every other page of the report and not a wider one.</para>
///
/// <para><b>The line height is where the page count comes from.</b> 12pt at 1.35 gives 16.2pt, and
/// 732 over that is 45 lines a page — which is what makes the manuscript about 140 pages (F45) and
/// not the 203 that was computed on the raster geometry this phase replaced. Changing the leading
/// changes the length of the document, so it is one number here rather than a literal in a
/// renderer.</para>
/// </summary>
public static class MarkupGeometry
{
    /// <summary>
    /// The report's content box — <see cref="PageGeometry"/>'s, not a second copy of it. The drawn
    /// page measures 489 × 732 because that is what 595 × 842 comes to inside the report's margins.
    /// </summary>
    public const double ContentWidth = PageGeometry.ContentWidth;

    public const double ContentHeight = PageGeometry.ContentHeight;

    /// <summary>The manuscript itself.</summary>
    public const double TextWidth = 316;

    /// <summary>The note column.</summary>
    public const double MarginWidth = 143;

    /// <summary>
    /// Air between the prose and the notes about it, where the leaders run. Whatever the box has
    /// left, so the three always tile it exactly and a rounded margin cannot push the notes off the
    /// sheet.
    /// </summary>
    public const double Gutter = ContentWidth - TextWidth - MarginWidth;

    /// <summary>Where the note column starts, measured from the content box's left edge.</summary>
    public const double MarginLeft = TextWidth + Gutter;

    /// <summary>
    /// The manuscript's own size. It is the printed ladder's Body rung — guarded rather than
    /// derived, because Domain holds no reference to the library the ladder lives in.
    /// </summary>
    public const double TypeSize = 12;

    public const double LineHeight = TypeSize * PageGeometry.LineSpacing;

    /// <summary>Lines to a page, if nothing on it has a note worth breaking for.</summary>
    public static int LinesPerPage => (int)(ContentHeight / LineHeight);

    /// <summary>The severity bracket beside a note, and how far out it sits.</summary>
    public const double BracketWidth = 2;

    public const double BracketInset = 2;
}
