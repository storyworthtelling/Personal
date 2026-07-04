using SkiaSharp;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.Design;

namespace Storyboarder.Infrastructure.Imaging;

// Renders the consolidated revision report into a sequence of PNG "pages" with SkiaSharp text drawing
// (the same on-device glyph path the comic pages already use). Layout flows top-to-bottom with
// automatic page breaks: headings, paragraphs, bullet lists, and per-character blocks form the
// character map. Styling follows DESIGN.md §7: paper pages, Source Serif type in ink, terracotta
// rules under section headings, and a footer with the running title and page number. The pages are
// handed to the existing IVolumePdfComposer to assemble the final PDF.
public sealed class AnalysisDocumentRenderer
{
    private static readonly SKColor Paper = SKColor.Parse(DesignTokens.Paper);
    private static readonly SKColor Ink = SKColor.Parse(DesignTokens.Ink);
    private static readonly SKColor Accent = SKColor.Parse(DesignTokens.Accent);
    private static readonly SKColor Muted = SKColor.Parse(DesignTokens.InkMuted);

    public IReadOnlyList<byte[]> Render(BookAnalysisResult analysis, string title)
    {
        using var body = new SKFont(EmbeddedTypefaces.SerifRegular, DesignTokens.Pdf.Body);
        using var bodyBold = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.Body);
        using var bodyItalic = new SKFont(EmbeddedTypefaces.SerifItalic, DesignTokens.Pdf.Body);
        using var h1 = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.H1);
        using var h2 = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.H2);
        using var h3 = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.H3);

        var reportTitle = string.IsNullOrWhiteSpace(title) ? "Book Analysis" : title;
        var writer = new PageWriter(reportTitle);

        writer.Heading(reportTitle, h1, Ink, ruled: true);
        writer.Paragraph("Revision report — a tool for revising the manuscript toward publication.", bodyItalic, Muted);
        writer.Gap(20f);

        if (!string.IsNullOrWhiteSpace(analysis.Logline))
        {
            writer.Heading("Logline", h2, Ink, ruled: true);
            writer.Paragraph(analysis.Logline, body, Ink);
        }
        if (!string.IsNullOrWhiteSpace(analysis.Synopsis))
        {
            writer.Heading("Synopsis", h2, Ink, ruled: true);
            writer.Paragraph(analysis.Synopsis, body, Ink);
        }

        if (analysis.Characters.Count > 0)
        {
            writer.Heading("Character map & arcs", h2, Ink, ruled: true);
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
            writer.Heading("Reviews (differing perspectives)", h2, Ink, ruled: true);
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
                    writer.Paragraph($"Recommendation: {review.Recommendation}", bodyItalic, Muted);
                writer.Gap(12f);
            }
        }

        if (analysis.RevisionPlan.Count > 0)
        {
            writer.Heading("Revision plan (highest impact first)", h2, Ink, ruled: true);
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
            writer.Heading("Positioning for publication", h2, Ink, ruled: true);
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
        writer.Heading(heading, h2, Ink, ruled: true);
        foreach (var item in items)
            writer.Bullet(item, body, Ink);
        writer.Gap(18f);
    }

    // Accumulates drawn text down a page, snapshotting to PNG and starting a fresh page when the
    // content cursor would overflow the bottom margin. All positions derive from DesignTokens.Pdf
    // so the page geometry stays in one place. Every finished page gets a footer: running title
    // left in muted ink, page number right in ink.
    private sealed class PageWriter
    {
        private const float ContentLeft = DesignTokens.Pdf.SideMargin;
        private const float ContentWidth = DesignTokens.Pdf.PageWidth - 2 * DesignTokens.Pdf.SideMargin;
        private const float ContentBottom = DesignTokens.Pdf.PageHeight - DesignTokens.Pdf.BottomMargin;

        private readonly string runningTitle;
        private readonly List<byte[]> pages = [];
        private SKSurface surface = null!;
        private SKCanvas canvas = null!;
        private float y;

        public PageWriter(string runningTitle)
        {
            this.runningTitle = runningTitle;
            StartPage();
        }

        public void Gap(float height) => y += height;

        public void Heading(string text, SKFont font, SKColor color, bool ruled = false)
        {
            y += 10f;
            // Keep a heading with at least the next line of content: break early if it would sit at
            // the very bottom of the page.
            if (y + font.Size * DesignTokens.Pdf.LineSpacing * 2 > ContentBottom)
                NewPage();
            DrawWrapped(text, font, color, ContentLeft, ContentWidth);
            if (ruled)
            {
                using var rule = new SKPaint { Color = Accent, IsAntialias = false };
                canvas.DrawRect(ContentLeft, y + 4f, ContentWidth / 3, DesignTokens.Pdf.AccentRuleHeight, rule);
                y += DesignTokens.Pdf.AccentRuleGap;
            }
            y += 6f;
        }

        public void Paragraph(string text, SKFont font, SKColor color)
        {
            DrawWrapped(text, font, color, ContentLeft, ContentWidth);
            y += 10f;
        }

        public void Bullet(string text, SKFont font, SKColor color)
        {
            float lineHeight = font.Size * DesignTokens.Pdf.LineSpacing;
            var lines = SkiaText.Wrap(font, text, ContentWidth - DesignTokens.Pdf.BulletIndent);
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            using var bulletPaint = new SKPaint { Color = Accent, IsAntialias = true };
            for (int i = 0; i < lines.Count; i++)
            {
                if (y + lineHeight > ContentBottom)
                    NewPage();
                float baseline = y + font.Size;
                if (i == 0)
                    canvas.DrawText("•", ContentLeft, baseline, font, bulletPaint);
                canvas.DrawText(lines[i], ContentLeft + DesignTokens.Pdf.BulletIndent, baseline, font, paint);
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
            float lineHeight = font.Size * DesignTokens.Pdf.LineSpacing;
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
            surface = SKSurface.Create(new SKImageInfo(DesignTokens.Pdf.PageWidth, DesignTokens.Pdf.PageHeight));
            canvas = surface.Canvas;
            canvas.Clear(Paper);
            y = DesignTokens.Pdf.TopMargin;
        }

        private void FinishPage()
        {
            DrawFooter();
            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                pages.Add(data.ToArray());
            surface.Dispose();
        }

        private void DrawFooter()
        {
            using var sans = new SKFont(EmbeddedTypefaces.SansRegular, DesignTokens.Pdf.Footer);
            using var serif = new SKFont(EmbeddedTypefaces.SerifRegular, DesignTokens.Pdf.Footer);
            float baseline = DesignTokens.Pdf.PageHeight - (DesignTokens.Pdf.BottomMargin - DesignTokens.Pdf.Footer) / 2;

            using var titlePaint = new SKPaint { Color = Muted, IsAntialias = true };
            canvas.DrawText(runningTitle, ContentLeft, baseline, sans, titlePaint);

            var pageNumber = (pages.Count + 1).ToString();
            using var numberPaint = new SKPaint { Color = Ink, IsAntialias = true };
            float numberWidth = serif.MeasureText(pageNumber);
            canvas.DrawText(pageNumber, DesignTokens.Pdf.PageWidth - DesignTokens.Pdf.SideMargin - numberWidth,
                baseline, serif, numberPaint);
        }

        private void NewPage()
        {
            FinishPage();
            StartPage();
        }
    }
}
