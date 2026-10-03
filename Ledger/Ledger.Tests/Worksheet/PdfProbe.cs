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

    public static float PageHeight(Page page) => (float)page.Height;

    public static (float Left, float Top, float Right, float Bottom) Box(Page page, UglyToad.PdfPig.Core.PdfRectangle r) =>
        ((float)r.Left, PageHeight(page) - (float)r.Top, (float)r.Right, PageHeight(page) - (float)r.Bottom);

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

    public static bool IsInk(Rule rule) => SameColor(rule.Color, Rgb(PaperColorTokens.Ink));
    public static bool IsHairline(Rule rule) => SameColor(rule.Color, Rgb(PaperColorTokens.Hairline));
}
