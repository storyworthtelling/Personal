using PdfSharp.Drawing;
using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Pdf;

// Draws the bottom "write below" area shared by the calendar and checklist documents:
// any low-confidence carryover notes for re-entry, a prompt label, and a bordered box that
// fills the remaining page for hand-writing future events/tasks.
internal static class HandwritingRegion
{
    private const double MinRegionHeight = 90;

    public static void Draw(PdfLayout layout, string prompt, IReadOnlyList<ParsedNote> carryovers)
    {
        var labelFont = new XFont("sans-serif", 11, XFontStyleEx.Bold);
        var noteFont = new XFont("sans-serif", 10, XFontStyleEx.Italic);

        // Ensure there is room for a usable region; otherwise start a fresh page.
        if (layout.BottomY - layout.Y < MinRegionHeight + 40)
            layout.NewPage();

        layout.Advance(10);

        if (carryovers.Count > 0)
        {
            layout.DrawWrapped("Couldn't read your last note(s) — please re-enter:", noteFont);
            foreach (var note in carryovers)
                layout.DrawWrapped($"• {note.Text}", noteFont);
            layout.Advance(6);
        }

        layout.Gfx.DrawString(prompt, labelFont, XBrushes.Black,
            new XRect(PdfLayout.Margin, layout.Y, layout.ContentWidth, 16), XStringFormats.TopLeft);
        layout.Advance(18);

        var top = layout.Y;
        var height = layout.BottomY - top;
        layout.Gfx.DrawRectangle(new XPen(XColors.Gray, 0.75),
            PdfLayout.Margin, top, layout.ContentWidth, height);
    }
}
