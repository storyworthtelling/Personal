using SkiaSharp;
using Foreword.Domain.Tasks;
using Storyworthtelling.Paper;

namespace Foreword.Infrastructure.Pdf;

/// <summary>
/// Page 4 of The Day (D4): the checklist, in three time bands side by side.
///
/// <para>Side by side rather than one long column because a real list runs longer than a short page
/// can hold in two. A task's band is inferred from the day's shape rather than set by hand, and its
/// kind is marked at the right.</para>
///
/// <para>The strip at the foot is for <b>adding</b> a task: ruled lines and no boxes, because
/// writing a task and ticking one are different acts and should not look alike — and a "Do on"
/// column, because a task written here may belong to any future day. Ticking happens on this page
/// and nowhere else; Foreword has no tick control anywhere in the app.</para>
/// </summary>
internal static class ChecklistPage
{
    private const double BandGap = 26;
    private const double RowHeight = 20;
    private const double BoxSize = 9;
    private const double AddStripHeight = 86;
    private const double NoteHeight = 10;

    public static void Draw(SkiaPageLayout layout, IReadOnlyList<PrintedTask> tasks, string voice)
    {
        if (!string.IsNullOrWhiteSpace(voice))
        {
            var voiceFont = SkiaPageLayout.Script(PaperTypeScale.Title);
            layout.Advance(4);
            layout.DrawText(voice, voiceFont, SkiaPageLayout.Margin, layout.Y,
                DayPageFurniture.ContentWidth(layout), SkiaPageLayout.LineHeight(voiceFont),
                TextAlign.TopLeft);
            layout.Advance(SkiaPageLayout.LineHeight(voiceFont) + 6);
        }

        var bands = Enum.GetValues<TaskBand>();
        var top = layout.Y;
        var bottom = layout.BottomY - AddStripHeight;
        var width = (DayPageFurniture.ContentWidth(layout) - BandGap * (bands.Length - 1)) / bands.Length;

        for (var i = 0; i < bands.Length; i++)
        {
            DrawBand(layout, bands[i],
                [.. tasks.Where(printed => TaskBands.For(printed.Task) == bands[i])],
                SkiaPageLayout.Margin + i * (width + BandGap), top, width, bottom);
        }

        DrawAddStrip(layout, bottom);
    }

    private static void DrawBand(
        SkiaPageLayout layout, TaskBand band, IReadOnlyList<PrintedTask> tasks,
        double x, double top, double width, double bottom)
    {
        var headingFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var taskFont = SkiaPageLayout.Sans(PaperTypeScale.Body);
        var kindFont = SkiaPageLayout.Sans(PaperTypeScale.Label);

        var hue = PaperPalette.ResolveRole(BandRole(band), ColorTier.Mark, UiColorProfile.DocumentColor);

        layout.DrawText(TaskBands.Heading(band).ToUpperInvariant(), headingFont, x, top,
            width, 11, TextAlign.TopLeft, hue);
        layout.DrawRect(x, top + 13, width, 1.6, fill: hue);

        var y = top + 18;

        foreach (var printed in tasks)
        {
            // A note needs a second line, and a row without one does not reserve the space.
            var hasNote = printed.Note.Length > 0;
            var rowHeight = hasNote ? RowHeight + NoteHeight : RowHeight;

            if (y + rowHeight > bottom)
            {
                break;
            }

            // The box. Empty, always: nothing in the app can tick one, so a filled box on this page
            // could only ever be something the app decided on the owner's behalf.
            layout.DrawRect(x, y + 5, BoxSize, BoxSize,
                stroke: SkiaPageLayout.Ink, strokeWidth: 0.7f);

            var kind = TaskBands.Kind(printed.Task);
            var kindWidth = layout.MeasureText(kind, kindFont) + 4;
            var textLeft = x + BoxSize + 6;
            var textWidth = width - BoxSize - 6 - kindWidth;

            layout.DrawText(
                layout.Truncate(printed.Task.Title, taskFont, textWidth),
                taskFont, textLeft, y + 3, textWidth, RowHeight - 4, TextAlign.TopLeft);

            layout.DrawText(kind, kindFont, x, y + 5, width, 11, TextAlign.TopRight,
                SkiaPageLayout.InkMuted);

            // Why it is here today. A list that schedules itself and will not explain itself is one
            // nobody trusts twice — and this is the line that explains it, on the task rather than
            // in a log the owner cannot reach from the page.
            if (hasNote)
            {
                layout.DrawText(
                    layout.Truncate(printed.Note, kindFont, width - BoxSize - 6),
                    kindFont, textLeft, y + RowHeight - 4, width - BoxSize - 6, NoteHeight,
                    TextAlign.TopLeft, SkiaPageLayout.InkMuted);
            }

            layout.DrawLine(x, y + rowHeight, x + width, y + rowHeight, SkiaPageLayout.Hairline, 0.5f);
            y += rowHeight;
        }

        if (tasks.Count == 0)
        {
            layout.DrawText("nothing", SkiaPageLayout.Sans(PaperTypeScale.Label), x, y + 4,
                width, 12, TextAlign.TopLeft, SkiaPageLayout.InkMuted);
        }
    }

    /// <summary>
    /// Where a new task is written. Ruled lines and no boxes, and a dated left column — a task
    /// written here may belong to any future day, and the read-back files it onto the day named.
    /// </summary>
    private static void DrawAddStrip(SkiaPageLayout layout, double top)
    {
        const double DateColumn = 86;
        const double LineGap = 24;

        var headingFont = SkiaPageLayout.Sans(PaperTypeScale.Label, bold: true);
        var right = DayPageFurniture.ContentRight(layout);

        layout.DrawText("Do on", headingFont, SkiaPageLayout.Margin, top + 8,
            DateColumn, 12, TextAlign.TopLeft, SkiaPageLayout.InkSoft);
        layout.DrawText("Add a task", headingFont, SkiaPageLayout.Margin + DateColumn + 10, top + 8,
            right - SkiaPageLayout.Margin - DateColumn - 10, 12, TextAlign.TopLeft,
            SkiaPageLayout.InkSoft);

        layout.DrawLine(SkiaPageLayout.Margin, top + 22, right, top + 22, SkiaPageLayout.Hairline, 0.5f);

        for (var y = top + 22 + LineGap; y <= layout.BottomY; y += LineGap)
        {
            layout.DrawLine(SkiaPageLayout.Margin, y, right, y, SkiaPageLayout.Hairline, 0.5f);

            // The tick separating the date column from the task, short, so it reads as a divider on
            // the line rather than as a second rule down the page.
            layout.DrawLine(SkiaPageLayout.Margin + DateColumn, y - 14,
                SkiaPageLayout.Margin + DateColumn, y, SkiaPageLayout.Hairline, 0.5f);
        }

        layout.MoveTo(layout.BottomY);
    }

    private static ColorRole BandRole(TaskBand band) => band switch
    {
        TaskBand.Morning => ColorRole.Caution,
        TaskBand.DuringTheDay => ColorRole.Action,
        _ => ColorRole.Reference,
    };
}
