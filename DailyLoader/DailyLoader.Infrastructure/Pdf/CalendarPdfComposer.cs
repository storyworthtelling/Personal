using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Pdf;

// Renders the daily calendar: page 1 is a five-column timeline (6 AM-10 PM) with a
// handwriting region at the bottom; page 2 is a seven-day outlook of non-routine events.
public class CalendarPdfComposer : ICalendarPdfComposer
{
    private const int StartHour = 6;
    private const int EndHour = 22;
    private const int HourSpan = EndHour - StartHour;
    private const double HourGutter = 46;
    private const double HeaderHeight = 18;
    private const double HandwritingReserve = 130;

    private static readonly PersonColumn[] Columns =
        [PersonColumn.Matt, PersonColumn.Ashley, PersonColumn.Gwen, PersonColumn.Eva, PersonColumn.Watch];

    public byte[] Compose(
        DayAgenda today,
        IReadOnlyList<CalendarEvent> outlook,
        IReadOnlyList<ParsedNote> unreadableCarryovers)
    {
        SystemFontResolver.Register();

        var doc = new PdfDocument();
        var layout = new PdfLayout(doc, PageOrientation.Landscape);

        DrawTimelinePage(layout, today);
        HandwritingRegion.Draw(layout, "Future events — write below:", unreadableCarryovers);

        layout.NewPage();
        DrawOutlookPage(layout, today.Date, outlook);

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static void DrawTimelinePage(PdfLayout layout, DayAgenda agenda)
    {
        var titleFont  = new XFont("sans-serif", 16, XFontStyleEx.Bold);
        var headerFont = new XFont("sans-serif", 11, XFontStyleEx.Bold);
        var hourFont   = new XFont("sans-serif",  8, XFontStyleEx.Regular);
        var eventFont  = new XFont("sans-serif",  8, XFontStyleEx.Regular);

        layout.DrawCentered(agenda.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy"), titleFont, 22);

        var gridTop = layout.Y;
        var gridLeft = PdfLayout.Margin;
        var gridRight = PdfLayout.Margin + layout.ContentWidth;
        var colsLeft = gridLeft + HourGutter;
        var colWidth = (gridRight - colsLeft) / Columns.Length;
        var timelineTop = gridTop + HeaderHeight;
        var gridBottom = layout.BottomY - HandwritingReserve;
        var hourHeight = (gridBottom - timelineTop) / HourSpan;

        var grayPen = new XPen(XColors.Gray, 0.5);
        var linePen = new XPen(XColors.LightGray, 0.5);

        // Column headers.
        for (var i = 0; i < Columns.Length; i++)
            layout.Gfx.DrawString(Columns[i].ToString(), headerFont, XBrushes.Black,
                new XRect(colsLeft + i * colWidth, gridTop, colWidth, HeaderHeight), XStringFormats.Center);

        // Hour rows + labels.
        for (var h = 0; h <= HourSpan; h++)
        {
            var y = timelineTop + h * hourHeight;
            layout.Gfx.DrawLine(linePen, colsLeft, y, gridRight, y);
            if (h < HourSpan)
                layout.Gfx.DrawString(FormatHour(StartHour + h), hourFont, XBrushes.Gray,
                    new XRect(gridLeft, y + 1, HourGutter - 4, 12), XStringFormats.TopRight);
        }

        // Column separators.
        for (var i = 0; i <= Columns.Length; i++)
        {
            var x = colsLeft + i * colWidth;
            layout.Gfx.DrawLine(grayPen, x, gridTop, x, gridBottom);
        }
        layout.Gfx.DrawLine(grayPen, gridLeft, gridTop, gridLeft, gridBottom);

        // All-day events stack as strips at the top of their column.
        var allDayOffset = new double[Columns.Length];
        foreach (var e in agenda.Events.Where(e => e.IsAllDay))
        {
            var col = ColumnIndex(e.Column);
            var x = colsLeft + col * colWidth;
            var y = timelineTop + allDayOffset[col];
            DrawEventBox(layout, x + 1, y, colWidth - 2, 13, e.Title, eventFont, XColor.FromArgb(230, 230, 245));
            allDayOffset[col] += 14;
        }

        // Timed events positioned by start/end within the timeline.
        foreach (var e in agenda.Events.Where(e => !e.IsAllDay))
        {
            var col = ColumnIndex(e.Column);
            var startOffset = HoursFromStart(e.Start);
            var endOffset = HoursFromStart(e.End);
            if (endOffset <= 0 || startOffset >= HourSpan)
                continue; // entirely outside the visible window

            startOffset = Math.Clamp(startOffset, 0, HourSpan);
            endOffset = Math.Clamp(endOffset, 0, HourSpan);

            var y = timelineTop + startOffset * hourHeight;
            var height = Math.Max(11, (endOffset - startOffset) * hourHeight);
            var x = colsLeft + col * colWidth;
            DrawEventBox(layout, x + 1, y, colWidth - 2, height, e.Title, eventFont, XColor.FromArgb(225, 238, 225));
        }

        layout.MoveTo(gridBottom);
    }

    private static void DrawOutlookPage(PdfLayout layout, DateOnly today, IReadOnlyList<CalendarEvent> outlook)
    {
        var titleFont = new XFont("sans-serif", 16, XFontStyleEx.Bold);
        var dayFont   = new XFont("sans-serif", 12, XFontStyleEx.Bold);
        var itemFont  = new XFont("sans-serif", 11, XFontStyleEx.Regular);

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
        PdfLayout layout, double x, double y, double width, double height, string title, XFont font, XColor fill)
    {
        layout.Gfx.DrawRectangle(new XSolidBrush(fill), x, y, width, height);
        layout.Gfx.DrawRectangle(new XPen(XColors.Gray, 0.4), x, y, width, height);
        layout.Gfx.DrawString(Truncate(layout.Gfx, title, font, width - 4), font, XBrushes.Black,
            new XRect(x + 2, y + 1, width - 4, Math.Min(height, 12)), XStringFormats.TopLeft);
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

    private static string Truncate(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        if (gfx.MeasureString(text, font).Width <= maxWidth || text.Length == 0)
            return text;

        var trimmed = text;
        while (trimmed.Length > 1 && gfx.MeasureString(trimmed + "…", font).Width > maxWidth)
            trimmed = trimmed[..^1];
        return trimmed + "…";
    }
}
