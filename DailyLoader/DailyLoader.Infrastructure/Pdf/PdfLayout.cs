using System.Text;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace DailyLoader.Infrastructure.Pdf;

// Shared page-layout state and text helpers for the PDF composers. Tracks the current page
// and vertical cursor, paginates automatically, and renders centered and word-wrapped text.
// Extracted from PdfComposer so the calendar and checklist composers can reuse it.
internal sealed class PdfLayout
{
    public const double Margin = 54; // 0.75 inch in points

    private readonly PdfDocument doc;
    private readonly PageOrientation orientation;

    public double PageWidth { get; }
    public double PageHeight { get; }
    public double ContentWidth => PageWidth - 2 * Margin;
    public double BottomY => PageHeight - Margin;

    public XGraphics Gfx { get; private set; }
    public double Y { get; private set; }

    public PdfLayout(PdfDocument doc, PageOrientation orientation = PageOrientation.Portrait)
    {
        this.doc = doc;
        this.orientation = orientation;
        PageWidth = orientation == PageOrientation.Portrait ? 612 : 792;   // US Letter in points
        PageHeight = orientation == PageOrientation.Portrait ? 792 : 612;
        Gfx = XGraphics.FromPdfPage(AddPage());
        Y = Margin;
    }

    public void NewPageIfNeeded(double spaceNeeded)
    {
        if (Y + spaceNeeded > BottomY)
            NewPage();
    }

    public void NewPage()
    {
        Gfx = XGraphics.FromPdfPage(AddPage());
        Y = Margin;
    }

    public void Advance(double amount) => Y += amount;

    public void MoveTo(double y) => Y = y;

    private PdfPage AddPage()
    {
        var page = doc.AddPage();
        page.Size = PageSize.Letter;
        page.Orientation = orientation;
        return page;
    }

    public void DrawCentered(string text, XFont font, double height)
    {
        NewPageIfNeeded(height);
        Gfx.DrawString(text, font, XBrushes.Black,
            new XRect(Margin, Y, ContentWidth, height), XStringFormats.TopCenter);
        Advance(height);
    }

    public void DrawWrapped(string text, XFont font, double lineSpacing = 1.4)
    {
        var lineH = font.GetHeight() * lineSpacing;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();

        foreach (var word in words)
        {
            var test = line.Length > 0 ? $"{line} {word}" : word;
            if (Gfx.MeasureString(test, font).Width > ContentWidth && line.Length > 0)
            {
                FlushLine(line.ToString(), font, lineH);
                line.Clear().Append(word);
            }
            else
            {
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
        }

        if (line.Length > 0)
            FlushLine(line.ToString(), font, lineH);
    }

    private void FlushLine(string line, XFont font, double lineH)
    {
        NewPageIfNeeded(lineH);
        Gfx.DrawString(line, font, XBrushes.Black,
            new XRect(Margin, Y, ContentWidth, lineH), XStringFormats.TopLeft);
        Advance(lineH);
    }
}
