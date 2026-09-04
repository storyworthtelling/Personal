using SkiaSharp;
using DailyLoader.Domain.Interfaces;
using DailyLoader.Domain.Models;

namespace DailyLoader.Infrastructure.Pdf;

// Renders the daily to-do checklist: the user's Google Tasks as checkbox lines, with a
// handwriting region at the bottom for jotting future tasks. Rendered with SkiaSharp via SkiaPageLayout.
public class ChecklistPdfComposer : IChecklistPdfComposer
{
    private const double BoxSize = 12;

    public byte[] Compose(
        IReadOnlyList<TodoItem> tasks,
        DateOnly date,
        IReadOnlyList<ParsedNote> unreadableCarryovers)
    {
        using var layout = new SkiaPageLayout();

        var titleFont = SkiaPageLayout.Sans(20, bold: true);
        var dateFont = SkiaPageLayout.Sans(12);
        var itemFont = SkiaPageLayout.Sans(12);

        layout.DrawCentered("To-Do Checklist", titleFont, 26);
        layout.DrawCentered(date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy"), dateFont, 18);
        layout.Advance(8);

        if (tasks.Count == 0)
            layout.DrawWrapped("No tasks for today.", itemFont);
        else
            foreach (var task in tasks)
                DrawCheckItem(layout, task, itemFont);

        HandwritingRegion.Draw(layout, "Future tasks — write below:", unreadableCarryovers);

        return layout.ToPdf();
    }

    private static void DrawCheckItem(SkiaPageLayout layout, TodoItem task, SKFont font)
    {
        layout.NewPageIfNeeded(22);

        layout.DrawRect(SkiaPageLayout.Margin, layout.Y + 2, BoxSize, BoxSize,
            stroke: SKColors.Black, strokeWidth: 1f);

        var text = task.Due is { } due ? $"{task.Title}  (due {due:MMM d})" : task.Title;
        var textLeft = SkiaPageLayout.Margin + BoxSize + 8;
        layout.DrawText(text, font, textLeft, layout.Y, layout.ContentWidth - BoxSize - 8, 16, TextAlign.TopLeft);

        layout.Advance(22);
    }
}
