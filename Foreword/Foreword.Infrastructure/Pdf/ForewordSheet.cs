using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// Foreword's sheets on the tablet's own page (audit 01 §12). Every composer is written in US Letter points; the PDF is
/// 445 × 594 (The Day: 594 × 445), scaled once, so the type is the size it always looked on the glass. The board grows
/// along its short side into the strip the tablet used to letterbox, so nothing that fitted on Letter stops fitting. The
/// journal's regions reach the vision read through <see cref="TabletSheet.ToPage"/>, in the page's own points.
/// </summary>
public static class ForewordSheet
{
    public static readonly TabletSheet Portrait = Storyworthtelling.Paper.Binder.SheetFrame.LetterBoard;

    public static readonly TabletSheet Landscape = TabletSheet.Fill(792f, 612f);
}
