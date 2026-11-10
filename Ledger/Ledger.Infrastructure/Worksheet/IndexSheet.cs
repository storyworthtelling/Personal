using Index.Domain.Worksheet;
using Storyworthtelling.Paper;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// The worksheet and follow-up page on the tablet's own page (audit 01 §12). Both are drawn on the A4 board the
/// mockups and <see cref="WorksheetRegions"/> are measured in, then placed on 445 × 594 with one scale and centred —
/// the type is the size it always looked on the glass, and the tablet no longer shrinks and letterboxes the sheet
/// itself. The read-back is told every box through <see cref="Box"/>, in the page's own points.
/// </summary>
public static class IndexSheet
{
    public static readonly TabletSheet Page = TabletSheet.Fit(WorksheetRegions.PageWidth, WorksheetRegions.PageHeight);

    public static WorksheetBox Box(WorksheetBox board)
    {
        var page = Page.ToPage(new SheetRect(board.Left, board.Top, board.Width, board.Height));
        return board with { Left = page.Left, Top = page.Top, Width = page.Width, Height = page.Height };
    }
}
