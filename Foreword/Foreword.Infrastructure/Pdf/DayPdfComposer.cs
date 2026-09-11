using SkiaSharp;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Models;

using Foreword.Domain.Calendar;
using Foreword.Domain.Document;
using Foreword.Domain.Weather;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// The Day — one four-page landscape document: the timeline, the weather, the outlook, the
/// checklist, bound together by the binder tabs down the right edge of every page.
///
/// <para>It was two documents. The calendar and the checklist were composed and uploaded
/// separately, which meant two things to find in the tablet's list every morning, two to trash the
/// next day, and no way to get from one to the other. P9 binds them, adds the weather page the
/// day's decisions actually turn on, and makes every page's tab strip a real PDF link.</para>
/// </summary>
public class DayPdfComposer : IDayPdfComposer
{
    private const int StartHour = 6;
    private const int EndHour = 22;
    private const int HourSpan = EndHour - StartHour;
    private const double HourGutter = 46;
    private const double HeaderHeight = 18;
    private const double HandwritingReserve = 130;

    // How far a meeting sits inside its work block: more on the left than the right, so the band's
    // own edge stays visible down the side the eye reads from.
    private const double NestIndent = 7;
    private const double NestInset = 4;

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
    // An unreachable drive is a caution, not a problem: the calendar is not wrong, the day is.
    private static readonly SKColor UnreachableMark =
        PaperPalette.ResolveRole(ColorRole.Caution, ColorTier.Mark, UiColorProfile.DocumentColor);

    private static readonly SKColor ContainerFill =
        PaperPalette.ResolveRole(ColorRole.Resolved, ColorTier.Wash, UiColorProfile.DocumentColor);

    // A weekend column on the outlook, tinted so the shape of the week reads before a word does.
    private static readonly SKColor OffDayFill =
        PaperPalette.ResolveRole(ColorRole.Resolved, ColorTier.Wash, UiColorProfile.DocumentColor);

    private static readonly PersonColumn[] Columns =
        [PersonColumn.Rowan, PersonColumn.Jordan, PersonColumn.Wren, PersonColumn.Linden, PersonColumn.Watch];

    public byte[] Compose(DayDocument document)
    {
        using var layout = new SkiaPageLayout(landscape: true);

        // Page 1 — the day itself.
        DayPageFurniture.Draw(layout, DaySection.Day, DayTitles.PageHeading(document.Date));
        DrawVoice(layout, document.Voice);
        DrawTimelinePage(layout, document.Agenda, document.UnreadableCarryovers);

        // Page 2 — the weather, on the timeline's own 6am-10pm axis so an event and the weather at
        // that hour line up vertically across the two sheets.
        layout.NewPage();
        DayPageFurniture.Draw(layout, DaySection.Weather, "Weather");
        WeatherPage.Draw(layout, document.Weather);

        // Page 3 — the week ahead.
        layout.NewPage();
        DayPageFurniture.Draw(layout, DaySection.Outlook, "The week ahead");
        DrawOutlookPage(layout, document.Date, document.Outlook);

        // Page 4 — the checklist. Ticking happens here and nowhere else: Foreword has no tick
        // control anywhere in the app.
        layout.NewPage();
        DayPageFurniture.Draw(layout, DaySection.Checklist, "Checklist");
        ChecklistPage.Draw(layout, document.Tasks, document.ChecklistVoice);

        return layout.ToPdf();
    }

    /// <summary>
    /// The narrator's line, in the owner's own hand. Nothing is drawn when there is none — a
    /// neutral sentence in the script face would be the app impersonating them, which is worse than
    /// a page that simply opens on the grid.
    /// </summary>
    private static void DrawVoice(SkiaPageLayout layout, string voice)
    {
        if (string.IsNullOrWhiteSpace(voice))
        {
            return;
        }

        var font = SkiaPageLayout.Script(PaperTypeScale.Title);
        layout.Advance(4);
        layout.DrawText(voice, font, SkiaPageLayout.Margin, layout.Y,
            DayPageFurniture.ContentWidth(layout), SkiaPageLayout.LineHeight(font),
            TextAlign.TopLeft);
        layout.Advance(SkiaPageLayout.LineHeight(font) + 4);
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

    private static void DrawTimelinePage(
        SkiaPageLayout layout, DayAgenda agenda, IReadOnlyList<ParsedNote> unreadableCarryovers)
    {
        var titleFont = SkiaPageLayout.Sans(PaperTypeScale.Title, bold: true);
        var headerFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary, bold: true);
        var hourFont = SkiaPageLayout.Sans(PaperTypeScale.Label);
        var eventFont = SkiaPageLayout.Sans(PaperTypeScale.Label);

        var stripHeight = StripHeight(eventFont);

        var gridTop = layout.Y;
        var gridLeft = SkiaPageLayout.Margin;
        // Stops clear of the binder edge rather than running under the tab strip.
        var gridRight = DayPageFurniture.ContentRight(layout);
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
            // Once in EVERY column it belongs to (D31). One event can be several people's.
            foreach (var col in e.Columns.Select(ColumnIndex).Where(c => c >= 0).Distinct())
            {
                var x = colsLeft + col * colWidth;
                var y = timelineTop + allDayOffset[col];
                DrawEventBox(layout, x + 1, y, colWidth - 2, stripHeight, e.Title, eventFont, AllDayFill);
                allDayOffset[col] += stripHeight + 1;
            }
        }

        // A shared event that can have one box gets one box (D32): the people are side by side and
        // alone in their hour, so it reads as one thing several of them are doing. Drawn before the
        // per-column pass so the columns it spans do not also draw it.
        // What could not be drawn legibly, written down under the grid instead. The owner asked for
        // all of them however many, and a list in full beats a box nobody can read.
        var alsoToday = new List<(CalendarEvent Event, PersonColumn Column)>();

        var timed = agenda.Events.Where(e => !e.IsAllDay).ToList();
        var spanned = timed.Where(e => SharedEventMode.Spans(e, timed)).ToList();

        foreach (var e in spanned)
        {
            var cols = e.Columns.Select(ColumnIndex).Where(c => c >= 0).Order().ToList();
            if (cols.Count == 0) continue;

            if (Window(e, hourHeight, timelineTop, stripHeight) is not { } window) continue;
            var (y, height) = window;

            var x = colsLeft + cols[0] * colWidth;
            DrawEventBox(layout, x + 1, y, colWidth * cols.Count - 2, height, e.Title, eventFont, TimedFill);

            // The corner tag names who it is for. A spanning box already shows WHICH columns, so
            // the tag is confirmation rather than the only signal — which is why it is allowed to
            // be truncated away on a narrow span rather than pushing the title out.
            layout.DrawText(layout.Truncate(Who(e), eventFont, colWidth * cols.Count - 4), eventFont,
                x + 1, y + 1, colWidth * cols.Count - 4, stripHeight, TextAlign.TopRight,
                SkiaPageLayout.InkMuted);
        }

        // The drive before an event, drawn as a hatched band in its column (Gate 1 · Q74). Under
        // the events, so a band never hides one; an unreachable drive takes the caution hue,
        // because that one is worth interrupting the page for.
        for (var col = 0; col < Columns.Length; col++)
        {
            var inColumn = timed.Where(e => e.Columns.Any(c => ColumnIndex(c) == col)).ToList();

            // Per column, so each person's drive starts where THEIR day has left them — and so an
            // event both a child and a driving parent are on gets a band in both their columns,
            // each measured from where that person actually is.
            foreach (var travel in TravelPlan.For(inColumn, agenda.Travel.Home, agenda.Travel.Minutes))
            {
                if (Window(travel.Event, hourHeight, timelineTop, stripHeight) is not { } at)
                {
                    continue;
                }

                var height = travel.Minutes / 60.0 * hourHeight;
                var top = at.Y - height;
                if (top < timelineTop)
                {
                    // Clipped at the top of the day rather than drawn above it: a band that ran off
                    // the grid would read as part of the header.
                    height -= timelineTop - top;
                    top = timelineTop;
                }

                if (height <= 0) continue;

                DrawTravelBand(layout, colsLeft + col * colWidth + 1, top, colWidth - 2, height,
                    travel.Unreachable);
            }
        }

        // Everything else, one column at a time so overlapping events share the width rather than
        // paint over each other (B2), and so a work block is drawn first, behind what sits inside it
        // (B3). EventLanes decides both; this only lays the answer out.
        for (var col = 0; col < Columns.Length; col++)
        {
            var inColumn = timed
                .Where(e => !spanned.Contains(e) && e.Columns.Any(c => ColumnIndex(c) == col))
                .ToList();

            var laid = EventLanes.Place(inColumn);

            // Anything past the legible width is written down instead of drawn (B2's other half).
            foreach (var extra in EventLanes.Overflow(laid))
            {
                alsoToday.Add((extra.Event, Columns[col]));
            }

            foreach (var placed in EventLanes.Drawn(laid))
            {
                var e = placed.Event;
                if (Window(e, hourHeight, timelineTop, stripHeight) is not { } window)
                    continue; // entirely outside the visible window

                var (y, height) = window;

                // A container spans the column; everything else takes its lane's share of it.
                var laneWidth = (colWidth - 2) / placed.Lanes;
                var x = colsLeft + col * colWidth + 1 + placed.Lane * laneWidth;

                // A meeting inside the work block is drawn INSIDE it, indented from the band's own
                // edges. Taking no lane from the block was only half of B3: without the indent the
                // two are drawn at the same width, one exactly over the other, so a nine-to-ten
                // standup covers the top of the nine-to-five instead of sitting in it — and the
                // band reads as something the meeting interrupted rather than as the day it
                // belongs to. An appointment that BREAKS the band is deliberately not indented: it
                // belongs to the day rather than to the work, and is drawn over the gap it makes.
                if (placed.IsNested)
                {
                    x += NestIndent;
                    laneWidth -= NestIndent + NestInset;
                }

                DrawEventBox(layout, x, y, laneWidth, height, e.Title, eventFont,
                    placed.IsContainer ? ContainerFill : TimedFill, placed.IsContainer);

                // A shared event drawn per column says who else is there (D32), under its title and
                // only if the box is tall enough to hold a second line — a "with" line that pushed
                // the title out would cost more than it explains.
                var with = SharedEventMode.WithLine(e, Columns[col]);
                if (with.Length > 0 && !placed.IsContainer && height >= stripHeight * 2)
                {
                    layout.DrawText(layout.Truncate(with, eventFont, laneWidth - 4), eventFont,
                        x + 2, y + stripHeight, laneWidth - 4, stripHeight, TextAlign.TopLeft,
                        SkiaPageLayout.InkMuted);
                }
            }
        }

        // The foot: what did not fit, what could not be read, and ruled space for future events.
        DayFoot.Draw(layout, gridBottom + 6, alsoToday, agenda.HeldCalendars, unreadableCarryovers);
    }

    /// <summary>
    /// The week ahead as seven columns rather than a list (D3): the shape of the week is the point,
    /// and a list of seven headings with two items under each hides it. Non-routine only — the
    /// selector has already dropped the recurring standup, so a column with nothing in it means a
    /// genuinely ordinary day and is allowed to say so.
    ///
    /// <para>There is no write-on space here. A future event is written once, on the ruled lines at
    /// the foot of the Day page, so there is only ever one place to put a pen.</para>
    /// </summary>
    private static void DrawOutlookPage(
        SkiaPageLayout layout, DateOnly today, IReadOnlyList<CalendarEvent> outlook)
    {
        var dayFont = SkiaPageLayout.Sans(PaperTypeScale.Secondary, bold: true);
        var dateFont = SkiaPageLayout.Sans(PaperTypeScale.Label);
        var itemFont = SkiaPageLayout.Sans(PaperTypeScale.Label);
        var noneFont = SkiaPageLayout.Sans(PaperTypeScale.Label);

        const int Days = 7;
        const double Gap = 7;
        const double HeaderHeight = 14;
        const double ItemGap = 3;
        const double RuleWidth = 2;

        var left = SkiaPageLayout.Margin;
        var top = layout.Y + 4;
        var width = (DayPageFurniture.ContentWidth(layout) - Gap * (Days - 1)) / Days;
        var height = layout.BottomY - top;

        for (var d = 0; d < Days; d++)
        {
            var date = today.AddDays(d);
            var x = left + d * (width + Gap);
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            // An off day carries a tinted ground, so the shape of the week is visible before a
            // single word on it is read.
            layout.DrawRect(x, top, width, height,
                fill: weekend ? OffDayFill : null,
                stroke: SkiaPageLayout.Hairline, strokeWidth: 0.5f);

            layout.DrawText(date.ToString("ddd"), dayFont, x + 4, top + 3,
                width - 8, HeaderHeight, TextAlign.TopLeft);
            layout.DrawText(date.ToString("MMM d"), dateFont, x + 4, top + 3,
                width - 8, HeaderHeight, TextAlign.TopRight, SkiaPageLayout.InkMuted);
            layout.DrawLine(x, top + HeaderHeight + 3, x + width, top + HeaderHeight + 3,
                SkiaPageLayout.Hairline, 0.5f);

            var dayEvents = outlook
                .Where(e => DateOnly.FromDateTime(e.Start) == date)
                .OrderBy(e => e.Start)
                .ToList();

            var y = top + HeaderHeight + 7;

            if (dayEvents.Count == 0)
            {
                // Wrapped, not drawn flat: a column is 94 points wide and this sentence is not.
                layout.DrawWrappedIn("nothing out of the ordinary", noneFont, x + 4, y,
                    width - 8, maxLines: 3, SkiaPageLayout.InkMuted);
                continue;
            }

            foreach (var e in dayEvents)
            {
                var text = e.IsAllDay ? e.Title : $"{e.Start:h:mm} {e.Title}";
                var textWidth = width - 12 - RuleWidth;

                // Measured before it is drawn, so an item that will not fit is left out rather
                // than drawn off the bottom of its column — and measured by WRAPPING it, since a
                // height guessed from the string's own width is right until the first word too
                // long to share a line.
                var itemHeight = layout.MeasureWrappedIn(text, itemFont, textWidth, maxLines: 3);

                if (y + itemHeight > top + height - 4)
                {
                    break;
                }

                // The left rule is the column the event belongs to, so a week's worth of "whose is
                // this" is answered by colour rather than by reading every line.
                layout.DrawRect(x + 4, y, RuleWidth, itemHeight, fill: ColumnMark(e));
                layout.DrawWrappedIn(text, itemFont, x + 4 + RuleWidth + 3, y, textWidth, maxLines: 3);

                y += itemHeight + ItemGap;
            }
        }

        layout.MoveTo(layout.BottomY);
    }

    /// <summary>
    /// The drive: a hatched band rather than a filled one, so it reads as time spoken for rather
    /// than as an event. Hatching is drawn as diagonal rules because a fill of any strength would
    /// compete with the boxes it sits under.
    /// </summary>
    private static void DrawTravelBand(
        SkiaPageLayout layout, double x, double y, double width, double height, bool unreachable)
    {
        var ink = unreachable ? UnreachableMark : SkiaPageLayout.InkMuted;

        layout.DrawRect(x, y, width, height, stroke: ink, strokeWidth: 0.4f);

        // Diagonals at a fixed spacing, clipped to the band by starting each where it enters.
        const double spacing = 4;
        for (var offset = 0.0; offset < width + height; offset += spacing)
        {
            var x1 = x + Math.Min(offset, width);
            var y1 = y + Math.Max(0, offset - width);
            var x2 = x + Math.Max(0, offset - height);
            var y2 = y + Math.Min(offset, height);

            layout.DrawLine(x1, y1, x2, y2, ink, 0.3f);
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

    /// <summary>
    /// Where an event sits in the timeline, or null if it falls entirely outside the visible window.
    /// Shared by both drawing modes so a spanning box and a per-column box cannot disagree about
    /// when the same event is.
    /// </summary>
    private static (double Y, double Height)? Window(
        CalendarEvent e, double hourHeight, double timelineTop, double stripHeight)
    {
        var startOffset = HoursFromStart(e.Start);
        var endOffset = HoursFromStart(e.End);
        if (endOffset <= 0 || startOffset >= HourSpan)
        {
            return null;
        }

        startOffset = Math.Clamp(startOffset, 0, HourSpan);
        endOffset = Math.Clamp(endOffset, 0, HourSpan);

        return (timelineTop + startOffset * hourHeight,
            Math.Max(stripHeight, (endOffset - startOffset) * hourHeight));
    }

    /// <summary>
    /// Who an event is for, as the outlook prints it. Everybody reads as "Everyone" rather than as
    /// four names, which is what a whole-family event actually means and a third of a line shorter.
    /// </summary>
    /// <summary>
    /// The hue of the column an event belongs to (§9), for the rule down the left of an outlook
    /// item. An event in several columns takes the first, because a two-colour rule two points wide
    /// is a smudge rather than a signal.
    /// </summary>
    private static SKColor ColumnMark(CalendarEvent e) =>
        PaperPalette.ResolveRole(
            (e.Columns.FirstOrDefault()) switch
            {
                PersonColumn.Rowan => ColorRole.Action,
                PersonColumn.Jordan => ColorRole.Reference,
                PersonColumn.Wren => ColorRole.Positive,
                PersonColumn.Linden => ColorRole.Caution,
                _ => ColorRole.Resolved,
            },
            ColorTier.Mark, UiColorProfile.DocumentColor);

    private static string Who(CalendarEvent e) =>
        ColumnSet.People.All(e.Columns.Contains)
            ? "Everyone"
            : string.Join(", ", e.Columns);

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
