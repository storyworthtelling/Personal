using SkiaSharp;
using Foreword.Domain.Models;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// The foot of page 1 (D1): what did not fit, what moved, and ruled space for future events.
///
/// <para>Two columns rather than one stacked under the other, because they answer opposite
/// questions and both have to be visible at a glance: the left is what the day already holds that
/// the grid could not draw, the right is empty space for something that is not today's.</para>
///
/// <para>The right column is <b>ruled lines, not a box</b>. A box says "write in here somewhere";
/// lines say how much room there is and where a line begins, which is what makes the read-back's job
/// possible — and each line carries a short stub near its left end, because what gets written here
/// is a future event and the day it belongs to is the part the reader has to supply.</para>
///
/// <para>It is the one write-on space for future events in the whole document. The outlook page
/// deliberately has none: one place to put a pen.</para>
/// </summary>
internal static class DayFoot
{
    private const double Gap = 30;
    private const double HeadingHeight = 12;
    private const double RuleGap = 13;
    private const double DateStub = 28;

    // The left column is narrower than the right: a list is read, and ruled lines are written on.
    private const double LeftShare = 0.9 / (0.9 + 1.1);

    public static void Draw(
        SkiaPageLayout layout,
        double top,
        IReadOnlyList<(CalendarEvent Event, PersonColumn Column)> alsoToday,
        IReadOnlyList<string> heldCalendars,
        IReadOnlyList<ParsedNote> unreadableCarryovers)
    {
        var headingFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var lineFont = SkiaPageLayout.Sans(PaperTypeScale.Label);

        var left = SkiaPageLayout.Margin;
        var right = DayPageFurniture.ContentRight(layout);
        var width = right - left;
        var leftWidth = (width - Gap) * LeftShare;
        var rightLeft = left + leftWidth + Gap;
        var rightWidth = right - rightLeft;

        layout.DrawLine(left, top, right, top, SkiaPageLayout.Hairline, 0.5f);

        DrawAlsoToday(layout, left, top + 8, leftWidth, headingFont, lineFont,
            alsoToday, heldCalendars, unreadableCarryovers);

        DrawWriteOn(layout, rightLeft, top + 8, rightWidth, headingFont);

        layout.MoveTo(layout.BottomY);
    }

    private static void DrawAlsoToday(
        SkiaPageLayout layout, double x, double y, double width,
        SKFont headingFont, SKFont lineFont,
        IReadOnlyList<(CalendarEvent Event, PersonColumn Column)> alsoToday,
        IReadOnlyList<string> heldCalendars,
        IReadOnlyList<ParsedNote> unreadableCarryovers)
    {
        // Nothing to say, so nothing is said. A heading standing over an empty space is noise, and
        // on an ordinary day this column is empty — which is what an ordinary day looks like.
        if (alsoToday.Count == 0 && unreadableCarryovers.Count == 0 && heldCalendars.Count == 0)
        {
            return;
        }

        var lineHeight = SkiaPageLayout.LineHeight(lineFont) + 1;
        var bottom = layout.BottomY;

        layout.DrawText("Also today", headingFont, x, y, width, HeadingHeight, TextAlign.TopLeft,
            SkiaPageLayout.InkSoft);
        y += HeadingHeight + 2;

        // Everything the grid could not hold at a legible width, in full, in time order. The owner
        // asked for all of them however many — a list in full is a better answer than a box nobody
        // can read.
        foreach (var (e, column) in alsoToday.OrderBy(entry => entry.Event.Start))
        {
            if (y + lineHeight > bottom) return;

            layout.DrawText(
                layout.Truncate($"{e.Start:h:mm}  {e.Title} — {column}", lineFont, width),
                lineFont, x, y, width, lineHeight, TextAlign.TopLeft);
            y += lineHeight;
        }

        // Yesterday's handwriting that could not be read, so it can be written again rather than
        // silently lost. Muted, because it is a note about the page rather than part of the day.
        foreach (var note in unreadableCarryovers)
        {
            if (y + lineHeight > bottom) return;

            layout.DrawText(
                layout.Truncate($"Couldn't read — {note.Text}", lineFont, width),
                lineFont, x, y, width, lineHeight, TextAlign.TopLeft, SkiaPageLayout.InkMuted);
            y += lineHeight;
        }

        // A held calendar is NAMED on the page (D34), because one the owner cannot see is the same
        // silence the holding exists to end.
        if (heldCalendars.Count > 0 && y + lineHeight <= bottom)
        {
            layout.DrawText(
                layout.Truncate(
                    $"Not printed — no column yet: {string.Join(", ", heldCalendars)}", lineFont, width),
                lineFont, x, y, width, lineHeight, TextAlign.TopLeft, SkiaPageLayout.InkMuted);
        }
    }

    private static void DrawWriteOn(
        SkiaPageLayout layout, double x, double y, double width, SKFont headingFont)
    {
        layout.DrawText("Future events — write below", headingFont, x, y, width, HeadingHeight,
            TextAlign.TopLeft, SkiaPageLayout.InkSoft);

        // As many lines as the space holds, rather than a fixed three: the reserve is a constant and
        // the page is not, so counting them here is what keeps the last line off the bottom edge.
        for (var line = y + HeadingHeight + RuleGap + 6; line <= layout.BottomY; line += RuleGap)
        {
            layout.DrawLine(x, line, x + width, line, SkiaPageLayout.Hairline, 0.5f);

            // The date stub: a short tick near the left end, because what is written here is a
            // future event and the day it belongs to is the part the reader has to supply.
            layout.DrawLine(x + DateStub, line - 9, x + DateStub, line, SkiaPageLayout.Hairline, 0.5f);
        }
    }
}
