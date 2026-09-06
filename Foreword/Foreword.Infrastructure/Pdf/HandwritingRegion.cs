using SkiaSharp;
using Foreword.Domain.Models;

using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

// Draws the bottom "write below" area shared by the calendar and checklist documents:
// any low-confidence carryover notes for re-entry, a prompt label, and a bordered box that
// fills the remaining page for hand-writing future events/tasks.
internal static class HandwritingRegion
{
    private const double MinRegionHeight = 90;

    public static void Draw(SkiaPageLayout layout, string prompt, IReadOnlyList<ParsedNote> carryovers)
    {
        var labelFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary, bold: true);
        // The prior design used an italic sans for notes; the embedded font set has no sans italic, so this
        // stays upright — a small, intentional deviation on secondary text.
        var noteFont = SkiaPageLayout.Sans(PaperTypeScale.Label);

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

        layout.DrawText(prompt, labelFont,
            SkiaPageLayout.Margin, layout.Y, layout.ContentWidth, 16, TextAlign.TopLeft);
        layout.Advance(18);

        var top = layout.Y;
        var height = layout.BottomY - top;
        layout.DrawRect(SkiaPageLayout.Margin, top, layout.ContentWidth, height,
            stroke: SkiaPageLayout.InkMuted, strokeWidth: 0.75f);
    }
}
