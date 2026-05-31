using PdfSharp.Drawing;
using PdfSharp.Pdf;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Pdf;

// Renders the daily to-do checklist: the user's Google Tasks as checkbox lines, with a
// handwriting region at the bottom for jotting future tasks.
public class ChecklistPdfComposer : IChecklistPdfComposer
{
    private const double BoxSize = 12;

    public byte[] Compose(
        IReadOnlyList<TodoItem> tasks,
        DateOnly date,
        IReadOnlyList<ParsedNote> unreadableCarryovers)
    {
        SystemFontResolver.Register();

        var doc = new PdfDocument();
        var layout = new PdfLayout(doc);

        var titleFont = new XFont("sans-serif", 20, XFontStyleEx.Bold);
        var dateFont  = new XFont("sans-serif", 12, XFontStyleEx.Regular);
        var itemFont  = new XFont("sans-serif", 12, XFontStyleEx.Regular);

        layout.DrawCentered("To-Do Checklist", titleFont, 26);
        layout.DrawCentered(date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy"), dateFont, 18);
        layout.Advance(8);

        if (tasks.Count == 0)
            layout.DrawWrapped("No tasks for today.", itemFont);
        else
            foreach (var task in tasks)
                DrawCheckItem(layout, task, itemFont);

        HandwritingRegion.Draw(layout, "Future tasks — write below:", unreadableCarryovers);

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static void DrawCheckItem(PdfLayout layout, TodoItem task, XFont font)
    {
        layout.NewPageIfNeeded(22);

        layout.Gfx.DrawRectangle(new XPen(XColors.Black, 1),
            PdfLayout.Margin, layout.Y + 2, BoxSize, BoxSize);

        var text = task.Due is { } due ? $"{task.Title}  (due {due:MMM d})" : task.Title;
        var textLeft = PdfLayout.Margin + BoxSize + 8;
        layout.Gfx.DrawString(text, font, XBrushes.Black,
            new XRect(textLeft, layout.Y, layout.ContentWidth - BoxSize - 8, 16), XStringFormats.TopLeft);

        layout.Advance(22);
    }
}
