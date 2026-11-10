using Storyworthtelling.Paper;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics;

namespace Index.Tests.Worksheet;

/// <summary>Reads painted geometry back out of a worksheet or follow-up PDF in page points from the top
/// left (PdfPig reports from the bottom left), so the cosmetic-drift tests assert what was painted
/// rather than what a constant says should have been.</summary>
internal static class PdfProbe
{
    internal sealed record Rule(float Left, float Top, float Right, float Bottom, double Width, (double R, double G, double B) Color)
    {
        public float Length => Right - Left;
        public float Y => (Top + Bottom) / 2f;
    }

    /// <summary>The PDF is the tablet's page; every drift test measures on the A4 board it was drawn on.</summary>
    public static readonly TabletSheet Sheet = Index.Infrastructure.Worksheet.IndexSheet.Page;

    public static float PageHeight(Page page) => (float)page.Height;

    /// <summary>A PDF rectangle (bottom-left, page points) as top-left board points.</summary>
    public static (float Left, float Top, float Right, float Bottom) Box(Page page, UglyToad.PdfPig.Core.PdfRectangle r) =>
        (BoardX((float)r.Left), BoardY(page, (float)r.Top), BoardX((float)r.Right), BoardY(page, (float)r.Bottom));

    public static float BoardX(float pageX) => (pageX - Sheet.OffsetX) / Sheet.Scale;

    public static float BoardY(Page page, float pdfY) => BoardY(pdfY, PageHeight(page));

    /// <summary>A PDF y (from the bottom) as a board y (from the top); every page is the tablet's height.</summary>
    public static float BoardY(float pdfY, float pageHeight = RemarkablePage.Height) => (pageHeight - pdfY - Sheet.OffsetY) / Sheet.Scale;

    /// <summary>A length on the page (a font size, a line width) as board points.</summary>
    public static float Board(double pageLength) => (float)(pageLength / Sheet.Scale);

    /// <summary>A box on the board as the read-back is told it: in the page's own points.</summary>
    public static Index.Domain.Worksheet.WorksheetBox OnPage(Index.Domain.Worksheet.WorksheetBox box)
    {
        var page = Sheet.ToPage(new SheetRect(box.Left, box.Top, box.Width, box.Height));
        return box with { Left = page.Left, Top = page.Top, Width = page.Width, Height = page.Height };
    }

    public static (double R, double G, double B) Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        return (Convert.ToInt32(h[..2], 16) / 255.0, Convert.ToInt32(h.Substring(2, 2), 16) / 255.0, Convert.ToInt32(h.Substring(4, 2), 16) / 255.0);
    }

    public static bool SameColor((double R, double G, double B) a, (double R, double G, double B) b) =>
        Math.Abs(a.R - b.R) < 0.012 && Math.Abs(a.G - b.G) < 0.012 && Math.Abs(a.B - b.B) < 0.012;

    /// <summary>Every stroked, flat (horizontal) line on the page.</summary>
    public static List<Rule> HorizontalRules(Page page)
    {
        var rules = new List<Rule>();
        foreach (var path in page.Paths)
        {
            if (!path.IsStroked || path.GetBoundingRectangle() is not { } rect)
            {
                continue;
            }
            var (left, top, right, bottom) = Box(page, rect);
            if (bottom - top > 0.01f)
            {
                continue;
            }
            var (r, g, b) = path.StrokeColor.ToRGBValues();
            rules.Add(new Rule(left, top, right, bottom, path.LineWidth, (r, g, b)));
        }
        return rules;
    }

    /// <summary>A letter of the frame's signature, the app's name in the owner's hand: its flourish may overhang the edge.</summary>
    public static bool IsSignature(UglyToad.PdfPig.Content.Letter letter) =>
        (letter.FontName ?? string.Empty).Contains("Pinyon", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every filled bar (a rectangle much wider than tall) on the page, with its fill colour, in board points.</summary>
    public static List<(float Left, float Top, float Right, float Bottom, (double R, double G, double B) Color)> Bars(Page page) =>
        page.Paths
            .Where(path => path.IsFilled && path.GetBoundingRectangle() is { } r && r.Width > 10 * r.Height)
            .Select(path =>
            {
                var (left, top, right, bottom) = Box(page, path.GetBoundingRectangle()!.Value);
                var (r, g, b) = path.FillColor.ToRGBValues();
                return (left, top, right, bottom, (r, g, b));
            })
            .ToList();

    public static bool IsInk(Rule rule) => SameColor(rule.Color, Rgb(PaperColorTokens.Ink));
    public static bool IsHairline(Rule rule) => SameColor(rule.Color, Rgb(PaperColorTokens.Hairline));
}
