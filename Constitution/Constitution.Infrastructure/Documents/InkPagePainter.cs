using Constitution.Domain.Documents;
using SkiaSharp;

namespace Constitution.Infrastructure.Documents;

/// <summary>
/// Paints one laid-out page: the header, every target from the map, and everything else printed. It
/// is handed a layout and nothing else, so it has no coordinate of its own to disagree with the
/// reader's. A page that breaks page 4's rules is refused here, with the reasons, rather than
/// printed and discovered on the tablet.
/// </summary>
internal static class InkPagePainter
{
    public static void Paint(SKCanvas canvas, InkPageLayout layout, DocumentInk ink)
    {
        var violations = layout.Violations();
        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"The page '{layout.Title}' breaks the ink-target rules and was not printed: "
                + string.Join("; ", violations.Select(v => $"{v.Kind} at {v.TargetId} ({v.Other})")));
        }

        DrawHeader(canvas, layout, ink);

        foreach (var target in layout.Map.Targets)
        {
            DrawTarget(canvas, target, ink);
        }

        foreach (var item in layout.Printed)
        {
            DrawPrinted(canvas, item, ink);
        }
    }

    private static void DrawHeader(SKCanvas canvas, InkPageLayout layout, DocumentInk ink)
    {
        var title = ConstitutionPage.TitleBounds;
        canvas.DrawText(layout.Title, title.Left, title.Bottom - ink.Title.Metrics.Descent, ink.Title, ink.Ink);
        canvas.DrawLine(
            ConstitutionPage.MarginLeft, ConstitutionPage.HeaderRuleTop,
            ConstitutionPage.Width - ConstitutionPage.MarginRight, ConstitutionPage.HeaderRuleTop,
            ink.HeaderRule);
    }

    /// <summary>
    /// A target is an outline, never a fill that hides the pen: ticks and the number box are outlined,
    /// the reason field is shaded and outlined. Lanes and the notes margin draw nothing of their own;
    /// their dividers and rules are printed items.
    /// </summary>
    private static void DrawTarget(SKCanvas canvas, InkTarget target, DocumentInk ink)
    {
        var rect = SKRect.Create(target.Bounds.Left, target.Bounds.Top, target.Bounds.Width, target.Bounds.Height);

        switch (target.Kind)
        {
            case InkTargetKind.LargeTick:
            case InkTargetKind.StandardTick:
            case InkTargetKind.NumberBox:
                canvas.DrawRect(rect, ink.TargetStroke);
                break;
            case InkTargetKind.NoBecause:
                canvas.DrawRect(rect, ink.Shade);
                canvas.DrawRect(rect, ink.TargetStroke);
                break;
        }
    }

    private static void DrawPrinted(SKCanvas canvas, PrintedItem item, DocumentInk ink)
    {
        if (item.Role == PrintedRole.Rule)
        {
            canvas.DrawLine(item.Bounds.Left, item.Bounds.Top, item.Bounds.Right, item.Bounds.Bottom, ink.DividerStroke);
            return;
        }

        if (string.IsNullOrEmpty(item.Text))
        {
            return;
        }

        var font = ink.FontFor(item.Role);
        var x = item.Align == PrintedAlign.Centre
            ? item.Bounds.CentreX - font.MeasureText(item.Text) / 2f
            : item.Bounds.Left;

        canvas.DrawText(item.Text, x, item.Bounds.Bottom - font.Metrics.Descent, font, ink.Ink);
    }
}
