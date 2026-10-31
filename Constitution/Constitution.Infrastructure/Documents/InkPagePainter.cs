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

        // Grounds first, so a tick on a shaded band is drawn over the shade and never under it.
        foreach (var item in layout.Printed.Where(i => i.Role == PrintedRole.Band))
        {
            canvas.DrawRect(SKRect.Create(item.Bounds.Left, item.Bounds.Top, item.Bounds.Width, item.Bounds.Height), ink.Shade);
        }

        var bands = layout.Printed.Where(i => i.Role == PrintedRole.Band).Select(i => i.Bounds).ToList();
        foreach (var target in layout.Map.Targets)
        {
            DrawTarget(canvas, target, ink, bands.Any(band => band.Encloses(target.Bounds)));
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
    private static void DrawTarget(SKCanvas canvas, InkTarget target, DocumentInk ink, bool onBand)
    {
        var rect = SKRect.Create(target.Bounds.Left, target.Bounds.Top, target.Bounds.Width, target.Bounds.Height);

        switch (target.Kind)
        {
            case InkTargetKind.LargeTick:
            case InkTargetKind.StandardTick:
            case InkTargetKind.NumberBox:
                if (onBand)
                {
                    canvas.DrawRect(rect, ink.Bright);
                }

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
        if (item.Role is PrintedRole.Rule or PrintedRole.Dotted)
        {
            var stroke = item.Role == PrintedRole.Dotted ? ink.DottedStroke : ink.DividerStroke;
            canvas.DrawLine(item.Bounds.Left, item.Bounds.Top, item.Bounds.Right, item.Bounds.Bottom, stroke);
            return;
        }

        if (item.Role is PrintedRole.MarkFilled or PrintedRole.MarkOpen)
        {
            var square = SKRect.Create(item.Bounds.Left, item.Bounds.Top, item.Bounds.Width, item.Bounds.Height);
            canvas.DrawRect(square, item.Role == PrintedRole.MarkFilled ? ink.Ink : ink.TargetStroke);
            return;
        }

        if (item.Role == PrintedRole.Chart)
        {
            DrawChart(canvas, item, ink);
            return;
        }

        if (item.Role is PrintedRole.Band or PrintedRole.Slot)
        {
            return;
        }

        if (string.IsNullOrEmpty(item.Text))
        {
            return;
        }

        var font = ink.FontFor(item.Role);
        var width = font.MeasureText(item.Text);
        var x = item.Align switch
        {
            PrintedAlign.Centre => item.Bounds.CentreX - width / 2f,
            PrintedAlign.Right => item.Bounds.Right - width,
            _ => item.Bounds.Left,
        };

        var colour = item.Role == PrintedRole.Small ? ink.Soft : ink.Ink;
        canvas.DrawText(item.Text, x, item.Bounds.Bottom - font.Metrics.Descent, font, colour);
    }

    /// <summary>The digest's weekly line: solid where there are readings, dotted across weeks with none, and the latest week as a dot (D39).</summary>
    private static void DrawChart(SKCanvas canvas, PrintedItem item, DocumentInk ink)
    {
        if (item.Points is null)
        {
            return;
        }

        var chart = DigestChartGeometry.Compute(item.Points, item.Bounds.Width, item.Bounds.Height);
        float X(double x) => item.Bounds.Left + (float)x;
        float Y(double y) => item.Bounds.Top + (float)y;

        foreach (var run in chart.Runs)
        {
            for (var index = 0; index + 1 < run.Count; index++)
            {
                canvas.DrawLine(X(run[index].X), Y(run[index].Y), X(run[index + 1].X), Y(run[index + 1].Y), ink.ChartStroke);
            }

            if (run.Count == 1)
            {
                canvas.DrawCircle(X(run[0].X), Y(run[0].Y), 1.5f, ink.Ink);
            }
        }

        foreach (var bridge in chart.Bridges)
        {
            canvas.DrawLine(X(bridge.From.X), Y(bridge.From.Y), X(bridge.To.X), Y(bridge.To.Y), ink.DottedStroke);
        }

        if (chart.Dot is { } dot)
        {
            canvas.DrawCircle(X(dot.X), Y(dot.Y), 3f, ink.Ink);
        }
    }
}
