using SkiaSharp;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;

using Foreword.Domain.Calendar;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

// Renders the daily calendar: page 1 is a five-column timeline (6 AM-10 PM) with a
// handwriting region at the bottom; page 2 is a seven-day outlook of non-routine events.
// Rendered with SkiaSharp via SkiaPageLayout.
public class CalendarPdfComposer : ICalendarPdfComposer
{
    private const int StartHour = 6;
    private const int EndHour = 22;
    private const int HourSpan = EndHour - StartHour;
    private const double HourGutter = 46;
    private const double HeaderHeight = 18;
    private const double HandwritingReserve = 130;

    // Event-box fills, kept as RGB components (as the PdfSharp version was) — not hex string literals.
    // The three fills, as roles at the WASH tier in the document profile — the highlighter band
    // DESIGN.md §3 puts behind full-strength ink, which is exactly what an event box is.
    //
    // They were a lavender and a green mixed by hand (230,230,245 and 225,238,225), no more part of
    // the palette than the named greys were and harder to notice, because a mixed colour reads as
    // deliberate. The colour guard only looked for SKColors. and walked straight past them.
    //
    // Reference for a thing that frames the day, Action for a real commitment in it, Resolved for
    // the work block, which is a backdrop rather than something to look at.
    private static readonly SKColor AllDayFill =
        PaperPalette.ResolveRole(ColorRole.Reference, ColorTier.Wash, UiColorProfile.DocumentColor);
    private static readonly SKColor TimedFill =
        PaperPalette.ResolveRole(ColorRole.Action, ColorTier.Wash, UiColorProfile.DocumentColor);
    private static readonly SKColor ContainerFill =
        PaperPalette.ResolveRole(ColorRole.Resolved, ColorTier.Wash, UiColorProfile.DocumentColor);

    private static readonly PersonColumn[] Columns =
        [PersonColumn.Rowan, PersonColumn.Jordan, PersonColumn.Wren, PersonColumn.Linden, PersonColumn.Watch];

    public byte[] Compose(
        DayAgenda today,
        IReadOnlyList<CalendarEvent> outlook,
        IReadOnlyList<ParsedNote> unreadableCarryovers)
    {
        using var layout = new SkiaPageLayout(landscape: true);

        DrawTimelinePage(layout, today);
        HandwritingRegion.Draw(layout, "Future events — write below:", unreadableCarryovers);

        layout.NewPage();
        DrawOutlookPage(layout, today.Date, outlook);

        return layout.ToPdf();
    }

    /// <summary>
    /// How tall an event strip is: its own text, plus a little.
    ///
    /// <para>Measured rather than assumed. The strips were fixed at 13 and 11 points, numbers chosen
    /// to suit an 8-point event face; the registered ladder (D30) puts that face at 9.75, and a box
    /// shorter than its own line clips the title <b>silently</b> — the page still renders, still
    /// paginates, and still passes every test that counts pages. This is the one place where
    /// re-fitting the type actually broke a sheet, which is what D30 warned would happen.</para>
    /// </summary>
    internal static float StripHeight(SKFont eventFont) => (float)SkiaPageLayout.LineHeight(eventFont) + 2;

    private static void DrawTimelinePage(SkiaPageLayout layout, DayAgenda agenda)
    {
        var titleFont = SkiaPageLayout.Sans(PaperTypeScale.Title, bold: true);
        var headerFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary, bold: true);
        var hourFont = SkiaPageLayout.Sans(PaperTypeScale.Label);
        var eventFont = SkiaPageLayout.Sans(PaperTypeScale.Label);

        var stripHeight = StripHeight(eventFont);

        layout.DrawCentered(agenda.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy"), titleFont, 22);

        var gridTop = layout.Y;
        var gridLeft = SkiaPageLayout.Margin;
        var gridRight = SkiaPageLayout.Margin + layout.ContentWidth;
        var colsLeft = gridLeft + HourGutter;
        var colWidth = (gridRight - colsLeft) / Columns.Length;
        var timelineTop = gridTop + HeaderHeight;
        var gridBottom = layout.BottomY - HandwritingReserve;
        var hourHeight = (gridBottom - timelineTop) / HourSpan;

        // Column headers.
        for (var i = 0; i < Columns.Length; i++)
            layout.DrawText(Columns[i].ToString(), headerFont,
                colsLeft + i * colWidth, gridTop, colWidth, HeaderHeight, TextAlign.Center);

        // Hour rows + labels.
        for (var h = 0; h <= HourSpan; h++)
        {
            var y = timelineTop + h * hourHeight;
            layout.DrawLine(colsLeft, y, gridRight, y, SkiaPageLayout.Hairline, 0.5f);
            if (h < HourSpan)
                layout.DrawText(FormatHour(StartHour + h), hourFont,
                    gridLeft, y + 1, HourGutter - 4, 12, TextAlign.TopRight, SkiaPageLayout.InkMuted);
        }

        // Column separators.
        for (var i = 0; i <= Columns.Length; i++)
        {
            var x = colsLeft + i * colWidth;
            layout.DrawLine(x, gridTop, x, gridBottom, SkiaPageLayout.InkMuted, 0.5f);
        }
        layout.DrawLine(gridLeft, gridTop, gridLeft, gridBottom, SkiaPageLayout.InkMuted, 0.5f);

        // All-day events stack as strips at the top of their column.
        var allDayOffset = new double[Columns.Length];
        foreach (var e in agenda.Events.Where(e => e.IsAllDay))
        {
            var col = ColumnIndex(e.Column);
            if (col < 0) continue;
            var x = colsLeft + col * colWidth;
            var y = timelineTop + allDayOffset[col];
            DrawEventBox(layout, x + 1, y, colWidth - 2, stripHeight, e.Title, eventFont, AllDayFill);
            allDayOffset[col] += stripHeight + 1;
        }

        // Timed events, one column at a time so overlapping events share the width rather than
        // paint over each other (B2), and so a work block is drawn first, behind what is inside it
        // (B3). EventLanes decides both; this only lays the answer out.
        for (var col = 0; col < Columns.Length; col++)
        {
            var inColumn = agenda.Events
                .Where(e => !e.IsAllDay && ColumnIndex(e.Column) == col)
                .ToList();

            foreach (var placed in EventLanes.Place(inColumn))
            {
                var e = placed.Event;
                var startOffset = HoursFromStart(e.Start);
                var endOffset = HoursFromStart(e.End);
                if (endOffset <= 0 || startOffset >= HourSpan)
                    continue; // entirely outside the visible window

                startOffset = Math.Clamp(startOffset, 0, HourSpan);
                endOffset = Math.Clamp(endOffset, 0, HourSpan);

                var y = timelineTop + startOffset * hourHeight;
                var height = Math.Max(stripHeight, (endOffset - startOffset) * hourHeight);

                // A container spans the column; everything else takes its lane's share of it.
                var laneWidth = (colWidth - 2) / placed.Lanes;
                var x = colsLeft + col * colWidth + 1 + placed.Lane * laneWidth;

                DrawEventBox(layout, x, y, laneWidth, height, e.Title, eventFont,
                    placed.IsContainer ? ContainerFill : TimedFill, placed.IsContainer);
            }
        }

        layout.MoveTo(gridBottom);
    }

    private static void DrawOutlookPage(SkiaPageLayout layout, DateOnly today, IReadOnlyList<CalendarEvent> outlook)
    {
        var titleFont = SkiaPageLayout.Sans(PaperTypeScale.Title, bold: true);
        var dayFont = SkiaPageLayout.Sans(PaperTypeScale.Body, bold: true);
        var itemFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary);

        layout.DrawCentered("7-Day Outlook", titleFont, 24);
        layout.Advance(6);

        for (var d = 0; d < 7; d++)
        {
            var date = today.AddDays(d);
            var dayEvents = outlook
                .Where(e => DateOnly.FromDateTime(e.Start) == date)
                .OrderBy(e => e.Start)
                .ToList();

            layout.NewPageIfNeeded(20);
            layout.DrawWrapped(date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d"), dayFont);

            if (dayEvents.Count == 0)
            {
                layout.DrawWrapped("    — nothing out of the ordinary", itemFont);
            }
            else
            {
                foreach (var e in dayEvents)
                {
                    var time = e.IsAllDay ? "All day" : e.Start.ToString("h:mm tt");
                    layout.DrawWrapped($"    • {time} — {e.Title} ({e.Column})", itemFont);
                }
            }

            layout.Advance(6);
        }
    }

    private static void DrawEventBox(
        SkiaPageLayout layout, double x, double y, double width, double height, string title,
        SKFont font, SKColor fill, bool isContainer = false)
    {
        layout.DrawRect(x, y, width, height, stroke: SkiaPageLayout.InkMuted, strokeWidth: 0.4f, fill: fill);

        // A container is a backdrop, so its own label sits at the FOOT of the box, out of the way of
        // the meetings drawn on top of it. Putting it at the head is what made the nine-to-five look
        // like an event that had swallowed the day.
        var textY = isContainer ? y + height - stripHeightFor(font) : y + 1;
        layout.DrawText(layout.Truncate(title, font, width - 4), font,
            x + 2, textY, width - 4, Math.Min(height, stripHeightFor(font)), TextAlign.TopLeft);
    }

    private static double stripHeightFor(SKFont font) => SkiaPageLayout.LineHeight(font);

    private static int ColumnIndex(PersonColumn column) => Array.IndexOf(Columns, column);

    private static double HoursFromStart(DateTime time) =>
        (time.Hour + time.Minute / 60.0) - StartHour;

    private static string FormatHour(int hour24)
    {
        var period = hour24 < 12 ? "AM" : "PM";
        var hour12 = hour24 % 12 == 0 ? 12 : hour24 % 12;
        return $"{hour12} {period}";
    }
}
