using SkiaSharp;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;

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
    private static readonly SKColor AllDayFill = new(230, 230, 245); // lavender
    private static readonly SKColor TimedFill = new(225, 238, 225);  // green

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

    private static void DrawTimelinePage(SkiaPageLayout layout, DayAgenda agenda)
    {
        var titleFont = SkiaPageLayout.Sans(16, bold: true);
        var headerFont = SkiaPageLayout.Sans(11, bold: true);
        var hourFont = SkiaPageLayout.Sans(8);
        var eventFont = SkiaPageLayout.Sans(8);

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
            layout.DrawLine(colsLeft, y, gridRight, y, SKColors.LightGray, 0.5f);
            if (h < HourSpan)
                layout.DrawText(FormatHour(StartHour + h), hourFont,
                    gridLeft, y + 1, HourGutter - 4, 12, TextAlign.TopRight, SKColors.Gray);
        }

        // Column separators.
        for (var i = 0; i <= Columns.Length; i++)
        {
            var x = colsLeft + i * colWidth;
            layout.DrawLine(x, gridTop, x, gridBottom, SKColors.Gray, 0.5f);
        }
        layout.DrawLine(gridLeft, gridTop, gridLeft, gridBottom, SKColors.Gray, 0.5f);

        // All-day events stack as strips at the top of their column.
        var allDayOffset = new double[Columns.Length];
        foreach (var e in agenda.Events.Where(e => e.IsAllDay))
        {
            var col = ColumnIndex(e.Column);
            if (col < 0) continue;
            var x = colsLeft + col * colWidth;
            var y = timelineTop + allDayOffset[col];
            DrawEventBox(layout, x + 1, y, colWidth - 2, 13, e.Title, eventFont, AllDayFill);
            allDayOffset[col] += 14;
        }

        // Timed events positioned by start/end within the timeline.
        foreach (var e in agenda.Events.Where(e => !e.IsAllDay))
        {
            var col = ColumnIndex(e.Column);
            if (col < 0) continue;
            var startOffset = HoursFromStart(e.Start);
            var endOffset = HoursFromStart(e.End);
            if (endOffset <= 0 || startOffset >= HourSpan)
                continue; // entirely outside the visible window

            startOffset = Math.Clamp(startOffset, 0, HourSpan);
            endOffset = Math.Clamp(endOffset, 0, HourSpan);

            var y = timelineTop + startOffset * hourHeight;
            var height = Math.Max(11, (endOffset - startOffset) * hourHeight);
            var x = colsLeft + col * colWidth;
            DrawEventBox(layout, x + 1, y, colWidth - 2, height, e.Title, eventFont, TimedFill);
        }

        layout.MoveTo(gridBottom);
    }

    private static void DrawOutlookPage(SkiaPageLayout layout, DateOnly today, IReadOnlyList<CalendarEvent> outlook)
    {
        var titleFont = SkiaPageLayout.Sans(16, bold: true);
        var dayFont = SkiaPageLayout.Sans(12, bold: true);
        var itemFont = SkiaPageLayout.Sans(11);

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
        SkiaPageLayout layout, double x, double y, double width, double height, string title, SKFont font, SKColor fill)
    {
        layout.DrawRect(x, y, width, height, stroke: SKColors.Gray, strokeWidth: 0.4f, fill: fill);
        layout.DrawText(layout.Truncate(title, font, width - 4), font,
            x + 2, y + 1, width - 4, Math.Min(height, 12), TextAlign.TopLeft);
    }

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
