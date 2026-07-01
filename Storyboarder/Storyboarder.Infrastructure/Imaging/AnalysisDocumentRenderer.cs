using SkiaSharp;
using Storyboarder.Domain.Analysis;

namespace Storyboarder.Infrastructure.Imaging;

// Renders the consolidated revision report into a sequence of PNG "pages" with SkiaSharp text drawing
// (the same on-device glyph path the comic pages already use — so no fonts to embed and no PdfSharp
// text/font-resolver machinery). Layout flows top-to-bottom with automatic page breaks: headings,
// paragraphs, bullet lists, and per-character blocks form the character map. The pages are handed to
// the existing IVolumePdfComposer to assemble the final PDF.
public sealed class AnalysisDocumentRenderer
{
    private const int PageWidth = 1240;   // ~A4 at 150 DPI, portrait
    private const int PageHeight = 1754;
    private const float Margin = 90f;
    private const float BodySize = 30f;
    private const float H1Size = 58f;
    private const float H2Size = 40f;
    private const float H3Size = 32f;

    private static readonly SKColor Ink = new(20, 20, 20);
    private static readonly SKColor HeadingColor = new(31, 45, 91);
    private static readonly SKColor Muted = new(90, 90, 90);

    public IReadOnlyList<byte[]> Render(BookAnalysisResult analysis, string title)
    {
        using var bold = SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                         ?? SKTypeface.Default;
        using var body = new SKFont(SKTypeface.Default, BodySize);
        using var bodyBold = new SKFont(bold, BodySize);
        using var h1 = new SKFont(bold, H1Size);
        using var h2 = new SKFont(bold, H2Size);
        using var h3 = new SKFont(bold, H3Size);

        var writer = new PageWriter();

        writer.Heading(string.IsNullOrWhiteSpace(title) ? "Book Analysis" : title, h1, HeadingColor);
        writer.Paragraph("Revision report — a tool for revising the manuscript toward publication.", body, Muted);
        writer.Gap(20f);

        if (!string.IsNullOrWhiteSpace(analysis.Logline))
        {
            writer.Heading("Logline", h2, HeadingColor);
            writer.Paragraph(analysis.Logline, body, Ink);
        }
        if (!string.IsNullOrWhiteSpace(analysis.Synopsis))
        {
            writer.Heading("Synopsis", h2, HeadingColor);
            writer.Paragraph(analysis.Synopsis, body, Ink);
        }

        if (analysis.Characters.Count > 0)
        {
            writer.Heading("Character map & arcs", h2, HeadingColor);
            foreach (var character in analysis.Characters)
            {
                var head = string.IsNullOrWhiteSpace(character.Role) ? character.Name : $"{character.Name} — {character.Role}";
                writer.Heading(head, h3, Ink);
                if (!string.IsNullOrWhiteSpace(character.ArcSummary))
                    writer.Paragraph(character.ArcSummary, body, Ink);
                foreach (var relationship in character.Relationships ?? [])
                    writer.Bullet(relationship, body, Ink);
                writer.Gap(12f);
            }
        }

        Section(writer, "Themes", analysis.Themes, body, h2);
        Section(writer, "Plot holes", analysis.PlotHoles, body, h2);
        Section(writer, "Inconsistencies", analysis.Inconsistencies, body, h2);
        Section(writer, "Inaccuracies to fact-check", analysis.Inaccuracies, body, h2);
        Section(writer, "Pacing", analysis.PacingNotes, body, h2);

        if (analysis.Reviews.Count > 0)
        {
            writer.Heading("Reviews (differing perspectives)", h2, HeadingColor);
            foreach (var review in analysis.Reviews)
            {
                writer.Heading(string.IsNullOrWhiteSpace(review.Perspective) ? "Review" : review.Perspective, h3, Ink);
                if (!string.IsNullOrWhiteSpace(review.Assessment))
                    writer.Paragraph(review.Assessment, body, Ink);
                if ((review.Strengths?.Count ?? 0) > 0)
                {
                    writer.Paragraph("Strengths:", bodyBold, Ink);
                    foreach (var strength in review.Strengths!)
                        writer.Bullet(strength, body, Ink);
                }
                if ((review.Weaknesses?.Count ?? 0) > 0)
                {
                    writer.Paragraph("Weaknesses:", bodyBold, Ink);
                    foreach (var weakness in review.Weaknesses!)
                        writer.Bullet(weakness, body, Ink);
                }
                if (!string.IsNullOrWhiteSpace(review.Recommendation))
                    writer.Paragraph($"Recommendation: {review.Recommendation}", body, Muted);
                writer.Gap(12f);
            }
        }

        if (analysis.RevisionPlan.Count > 0)
        {
            writer.Heading("Revision plan (highest impact first)", h2, HeadingColor);
            int number = 1;
            foreach (var item in analysis.RevisionPlan)
            {
                var label = string.IsNullOrWhiteSpace(item.Priority) ? $"{number}." : $"{number}. [{item.Priority}]";
                writer.Heading($"{label} {item.Issue}", h3, Ink);
                if (!string.IsNullOrWhiteSpace(item.SuggestedFix))
                    writer.Paragraph(item.SuggestedFix, body, Ink);
                writer.Gap(12f);
                number++;
            }
        }

        if (analysis.ComparableTitles.Count > 0 || !string.IsNullOrWhiteSpace(analysis.MarketPositioning))
        {
            writer.Heading("Positioning for publication", h2, HeadingColor);
            if (!string.IsNullOrWhiteSpace(analysis.MarketPositioning))
                writer.Paragraph(analysis.MarketPositioning, body, Ink);
            if (analysis.ComparableTitles.Count > 0)
            {
                writer.Paragraph("Comparable titles:", bodyBold, Ink);
                foreach (var comparable in analysis.ComparableTitles)
                    writer.Bullet(comparable, body, Ink);
            }
        }

        return writer.Finish();
    }

    private static void Section(PageWriter writer, string heading, IReadOnlyList<string> items, SKFont body, SKFont h2)
    {
        if (items.Count == 0)
            return;
        writer.Heading(heading, h2, HeadingColor);
        foreach (var item in items)
            writer.Bullet(item, body, Ink);
        writer.Gap(18f);
    }

    // Accumulates drawn text down a page, snapshotting to PNG and starting a fresh page when the
    // content cursor would overflow the bottom margin. All positions derive from the outer layout
    // constants so the page geometry stays in one place.
    private sealed class PageWriter
    {
        private const float LineSpacing = 1.35f;
        private const float BulletIndent = 34f;
        private const float ContentLeft = Margin;
        private const float ContentWidth = PageWidth - 2 * Margin;
        private const float ContentBottom = PageHeight - Margin;

        private readonly List<byte[]> pages = [];
        private SKSurface surface = null!;
        private SKCanvas canvas = null!;
        private float y;

        public PageWriter() => StartPage();

        public void Gap(float height) => y += height;

        public void Heading(string text, SKFont font, SKColor color)
        {
            y += 10f;
            // Keep a heading with at least the next line of content: break early if it would sit at
            // the very bottom of the page.
            if (y + font.Size * LineSpacing * 2 > ContentBottom)
                NewPage();
            DrawWrapped(text, font, color, ContentLeft, ContentWidth);
            y += 6f;
        }

        public void Paragraph(string text, SKFont font, SKColor color)
        {
            DrawWrapped(text, font, color, ContentLeft, ContentWidth);
            y += 10f;
        }

        public void Bullet(string text, SKFont font, SKColor color)
        {
            float lineHeight = font.Size * LineSpacing;
            var lines = SkiaText.Wrap(font, text, ContentWidth - BulletIndent);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            for (int i = 0; i < lines.Count; i++)
            {
                if (y + lineHeight > ContentBottom)
                    NewPage();
                float baseline = y + font.Size;
                if (i == 0)
                    canvas.DrawText("•", ContentLeft, baseline, font, paint);
                canvas.DrawText(lines[i], ContentLeft + BulletIndent, baseline, font, paint);
                y += lineHeight;
            }
            y += 4f;
        }

        public IReadOnlyList<byte[]> Finish()
        {
            FinishPage();
            return pages;
        }

        private void DrawWrapped(string text, SKFont font, SKColor color, float x, float wrapWidth)
        {
            float lineHeight = font.Size * LineSpacing;
            var lines = SkiaText.Wrap(font, text, wrapWidth);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            foreach (var line in lines)
            {
                if (y + lineHeight > ContentBottom)
                    NewPage();
                canvas.DrawText(line, x, y + font.Size, font, paint);
                y += lineHeight;
            }
        }

        private void StartPage()
        {
            surface = SKSurface.Create(new SKImageInfo(PageWidth, PageHeight));
            canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            y = Margin;
        }

        private void FinishPage()
        {
            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                pages.Add(data.ToArray());
            surface.Dispose();
        }

        private void NewPage()
        {
            FinishPage();
            StartPage();
        }
    }
}
