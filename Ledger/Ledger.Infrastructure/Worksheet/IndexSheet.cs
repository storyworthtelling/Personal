using Index.Domain.Worksheet;
using Storyworthtelling.Paper;
using Storyworthtelling.Paper.Binder;

namespace Index.Infrastructure.Worksheet;

/// <summary>
/// The worksheet and follow-up page on the tablet's own page (audit 01 §12), in the frame every tablet document wears.
/// Both are drawn on the board <see cref="WorksheetRegions"/> is measured in — A4's height, as wide as the glass is at
/// that scale — and placed on 445 × 594 with one scale, so the tabs sit at the glass's edge and the type is the size it
/// always looked. The read-back is told every box through <see cref="Box"/>, in the page's own points.
/// </summary>
public static class IndexSheet
{
    public static readonly TabletSheet Page = TabletSheet.Fill(WorksheetRegions.PageWidth, WorksheetRegions.PageHeight);

    /// <summary>Index signs its pages in its identity green; its tab is cut from the ground and lettered in the deep.</summary>
    public static readonly SheetSignature Signature =
        new("Index", AppIdentityTokens.IndexMark, AppIdentityTokens.IndexGround, AppIdentityTokens.IndexDeep);

    public static WorksheetBox Box(WorksheetBox board)
    {
        var page = Page.ToPage(new SheetRect(board.Left, board.Top, board.Width, board.Height));
        return board with { Left = page.Left, Top = page.Top, Width = page.Width, Height = page.Height };
    }

    /// <summary>One tab per sheet of the worksheet: the budget, and the sheet it continues onto when it needs one.</summary>
    public static IReadOnlyList<BinderTab> WorksheetTabs(int pageCount) =>
        Enumerable.Range(1, pageCount)
            .Select(page => new BinderTab(page == 1 ? "Budget" : "Continued", $"index-worksheet-{page}", Signature.Cloth, Signature.Deep))
            .ToList();

    /// <summary>The follow-up page is a document of one sheet, and wears its own tab.</summary>
    public static readonly IReadOnlyList<BinderTab> FollowUpTabs =
        [new BinderTab("Follow-up", "index-follow-up", Signature.Cloth, Signature.Deep)];
}
