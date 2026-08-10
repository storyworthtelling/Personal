using SkiaSharp;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.Design;
using Storyboarder.Domain.Imaging;

namespace Storyboarder.Infrastructure.Imaging;

// Renders the revision report — the star product — into a sequence of PNG "pages" with SkiaSharp. The
// report is layered: the analytical/thematic sections first, then the scene-by-scene walk where each
// scene's wordless evidence image sits above its notes (the fidelity check), then what-to-do (the
// revision plan by impact tier), and the candid verdict last. Styling follows DESIGN.md §7. The pages are
// handed to the existing IVolumePdfComposer to assemble the final PDF.
public sealed record AnalysisRenderResult(
    IReadOnlyList<byte[]> Pages,
    int FrontMatterCount,
    IReadOnlyList<(string Title, int Page)> TocEntries);

public sealed class AnalysisDocumentRenderer
{
    private static readonly SKColor Paper = SKColor.Parse(DesignTokens.Paper);
    private static readonly SKColor Ink = SKColor.Parse(DesignTokens.Ink);
    private static readonly SKColor Accent = SKColor.Parse(DesignTokens.Accent);
    private static readonly SKColor Muted = SKColor.Parse(DesignTokens.InkMuted);

    private readonly CoverPageRenderer covers = new();

    public IReadOnlyList<byte[]> Render(BookAnalysisResult analysis, string title, IReadOnlyList<SceneEvidence> scenes) =>
        RenderReport(analysis, title, scenes).Pages;

    /// <summary>
    /// Renders content pages first (recording each H2 section's page number),
    /// then prepends unnumbered front matter: cover + table of contents.
    /// Content folios stay 1..N, so the printed TOC needs no bookkeeping.
    /// </summary>
    public AnalysisRenderResult RenderReport(BookAnalysisResult analysis, string title, IReadOnlyList<SceneEvidence> scenes)
    {
        var reportTitle = string.IsNullOrWhiteSpace(title) ? "Book Analysis" : title;
        var content = RenderContent(analysis, reportTitle, scenes, out var tocEntries);

        var pages = new List<byte[]> { covers.AnalysisCover(reportTitle) };
        var toc = covers.AnalysisToc(tocEntries);
        pages.AddRange(toc);
        pages.AddRange(content);
        return new AnalysisRenderResult(pages, 1 + toc.Count, tocEntries);
    }

    private IReadOnlyList<byte[]> RenderContent(
        BookAnalysisResult analysis, string reportTitle, IReadOnlyList<SceneEvidence> scenes,
        out IReadOnlyList<(string Title, int Page)> tocEntries)
    {
        using var body = new SKFont(EmbeddedTypefaces.SerifRegular, DesignTokens.Pdf.Body);
        using var bodyBold = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.Body);
        using var bodyItalic = new SKFont(EmbeddedTypefaces.SerifItalic, DesignTokens.Pdf.Body);
        using var h1 = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.H1);
        using var h2 = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.H2);
        using var h3 = new SKFont(EmbeddedTypefaces.SerifSemibold, DesignTokens.Pdf.H3);

        var writer = new PageWriter(reportTitle);

        writer.Heading(reportTitle, h1, Ink, ruled: true);
        writer.Paragraph("Revision report — a candid second read to help you revise toward publication.", bodyItalic, Muted);

        // Genre + length, framed against the genre norm.
        if (!string.IsNullOrWhiteSpace(analysis.Genre) || analysis.WordCount > 0)
        {
            var lengthText = analysis.WordCount > 0 ? $"{analysis.WordCount:N0} words" : string.Empty;
            if (!string.IsNullOrWhiteSpace(analysis.WordCountRange))
                lengthText = string.IsNullOrEmpty(lengthText)
                    ? $"genre norm {analysis.WordCountRange}"
                    : $"{lengthText} (genre norm {analysis.WordCountRange})";
            writer.Paragraph(Join(analysis.Genre, lengthText), bodyBold, Ink);
        }
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

        if (analysis.Characters is { Count: > 0 })
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

        // Story integrity — the top analytical priority.
        Section(writer, "Plot holes", analysis.PlotHoles, body, h2);
        Section(writer, "Inconsistencies", analysis.Inconsistencies, body, h2);
        Section(writer, "Inaccuracies", analysis.Inaccuracies, body, h2);

        Section(writer, "Themes", analysis.Themes, body, h2);

        // Pacing, with a tension-over-chapters chart when the model returned a curve.
        if (analysis.PacingNotes is { Count: > 0 } || analysis.PacingCurve is { Count: > 0 })
        {
            writer.Heading("Pacing", h2, Ink, ruled: true);
            if (analysis.PacingCurve is { Count: > 0 })
            {
                writer.Paragraph("Tension across the chapters — a dip is a place to check the story is still pulling.", bodyItalic, Muted);
                writer.PacingChart(analysis.PacingCurve);
            }
            foreach (var note in analysis.PacingNotes ?? [])
                writer.Bullet(Join(note.Chapter, note.Detail), body, Ink);
            writer.Gap(18f);
        }

        if (analysis.ProseNotes is { Count: > 0 })
        {
            writer.Heading("Prose style & voice", h2, Ink, ruled: true);
            foreach (var note in analysis.ProseNotes)
            {
                writer.Bullet(Join(note.Chapter, note.Pattern, note.Evidence), body, Ink);
                if (!string.IsNullOrWhiteSpace(note.Fix))
                    writer.Paragraph(note.Fix, bodyItalic, Muted);
            }
            writer.Gap(18f);
        }

        if (analysis.DialogueNotes is { Count: > 0 })
        {
            writer.Heading("Dialogue", h2, Ink, ruled: true);
            foreach (var note in analysis.DialogueNotes)
            {
                writer.Bullet(Join(note.Chapter, note.Speakers, note.Observation), body, Ink);
                if (!string.IsNullOrWhiteSpace(note.Fix))
                    writer.Paragraph(note.Fix, bodyItalic, Muted);
            }
            writer.Gap(18f);
        }

        if (analysis.LineEdits is { Count: > 0 })
        {
            writer.Heading("Line-level edits", h2, Ink, ruled: true);
            writer.Paragraph("Sentences to cut, tighten, or rewrite. The excerpt is quoted as written; the words stay yours.", bodyItalic, Muted);
            foreach (var edit in analysis.LineEdits)
            {
                writer.Bullet(Join(edit.Chapter, Quoted(edit.Excerpt)), bodyItalic, Ink);
                if (!string.IsNullOrWhiteSpace(edit.Suggestion))
                    writer.Paragraph(edit.Suggestion, body, Ink);
                if (!string.IsNullOrWhiteSpace(edit.Reason))
                    writer.Paragraph(edit.Reason, bodyItalic, Muted);
                writer.Gap(8f);
            }
        }

        RenderVoiceConsistency(writer, analysis.VoiceConsistency, body, bodyBold, bodyItalic, h2);
        RenderReviews(writer, analysis.Reviews, body, bodyBold, bodyItalic, h2, h3);

        // The scene-by-scene walk: each scene's evidence image above its notes — the fidelity check.
        RenderScenes(writer, scenes, body, bodyItalic, h2);

        // What to do next: the revision plan grouped by impact tier.
        RenderRevisionPlan(writer, analysis.RevisionPlan, body, h2, h3);

        // The candid state of the draft, last.
        RenderVerdict(writer, analysis.Verdict, body, bodyBold, bodyItalic, h2);

        var pages = writer.Finish();
        tocEntries = writer.TocEntries;
        return pages;
    }

    private static void RenderScenes(
        PageWriter writer, IReadOnlyList<SceneEvidence> scenes, SKFont body, SKFont bodyItalic, SKFont h2)
    {
        if (scenes is not { Count: > 0 })
            return;

        writer.Heading("Scene by scene", h2, Ink, ruled: true);
        writer.Paragraph(
            "Each scene's wordless image is what a reader pictures from your words. Where it doesn't match " +
            "your mind's eye, the prose is under-specifying the beat.",
            bodyItalic, Muted);

        foreach (var scene in scenes)
        {
            writer.KeepTogether(DesignTokens.Pdf.SceneImageHeight + 80f);
            writer.Paragraph(string.IsNullOrWhiteSpace(scene.Citation) ? "Scene" : scene.Citation, bodyItalic, Accent);
            if (scene.Image is { Length: > 0 })
                writer.Image(scene.Image);
            else
                writer.Paragraph("Image unavailable for this scene.", bodyItalic, Muted);
            if (!string.IsNullOrWhiteSpace(scene.Purpose))
                writer.Paragraph(scene.Purpose, body, Ink);
            if (!string.IsNullOrWhiteSpace(scene.Verdict))
                writer.Paragraph(scene.Verdict, bodyItalic, Muted);
            writer.Gap(18f);
        }
    }

    private static void RenderVoiceConsistency(
        PageWriter writer, VoiceConsistency voice, SKFont body, SKFont bodyBold, SKFont bodyItalic, SKFont h2)
    {
        if (string.IsNullOrWhiteSpace(voice.DominantVoice) && voice.Divergences is not { Count: > 0 })
            return;

        writer.Heading("Authorial voice consistency", h2, Ink, ruled: true);
        writer.Paragraph(
            "Where the manuscript's own prose drifts from its dominant voice — a register slip, a tonal " +
            "wobble. Deliberately distinct voices (a different POV, an embedded letter) are left out.",
            bodyItalic, Muted);
        if (!string.IsNullOrWhiteSpace(voice.DominantVoice))
        {
            writer.Paragraph("The book's voice:", bodyBold, Ink);
            writer.Paragraph(voice.DominantVoice, body, Ink);
        }
        if (voice.Divergences is { Count: > 0 })
        {
            writer.Paragraph("Passages that drift:", bodyBold, Ink);
            foreach (var divergence in voice.Divergences)
            {
                writer.Bullet(Join(divergence.Chapter, Quoted(divergence.Excerpt)), bodyItalic, Ink);
                if (!string.IsNullOrWhiteSpace(divergence.Divergence))
                    writer.Paragraph(divergence.Divergence, body, Ink);
                if (!string.IsNullOrWhiteSpace(divergence.Suggestion))
                    writer.Paragraph(divergence.Suggestion, bodyItalic, Muted);
                writer.Gap(8f);
            }
        }
    }

    private static void RenderReviews(
        PageWriter writer, IReadOnlyList<Review> reviews, SKFont body, SKFont bodyBold, SKFont bodyItalic, SKFont h2, SKFont h3)
    {
        if (reviews is not { Count: > 0 })
            return;

        writer.Heading("Reviews (differing perspectives)", h2, Ink, ruled: true);
        foreach (var review in reviews)
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

    // The revision plan grouped by impact tier: Must, then Should, then Consider (anything else last).
    private static readonly string[] Tiers = ["Must", "Should", "Consider"];

    private static void RenderRevisionPlan(
        PageWriter writer, IReadOnlyList<RevisionItem> plan, SKFont body, SKFont h2, SKFont h3)
    {
        if (plan is not { Count: > 0 })
            return;

        writer.Heading("What to do next", h2, Ink, ruled: true);
        writer.Paragraph("The revision plan, grouped by impact.", new SKFont(EmbeddedTypefaces.SerifItalic, DesignTokens.Pdf.Body), Muted);

        bool Matches(RevisionItem item, string tier) =>
            string.Equals((item.Priority ?? string.Empty).Trim(), tier, StringComparison.OrdinalIgnoreCase);

        foreach (var tier in Tiers)
        {
            var items = plan.Where(i => Matches(i, tier)).ToList();
            if (items.Count == 0)
                continue;
            writer.Heading(tier, h3, Accent);
            foreach (var item in items)
            {
                writer.Bullet(Join(item.Chapter, item.Issue), body, Ink);
                if (!string.IsNullOrWhiteSpace(item.SuggestedFix))
                    writer.Paragraph(item.SuggestedFix, new SKFont(EmbeddedTypefaces.SerifItalic, DesignTokens.Pdf.Body), Muted);
            }
            writer.Gap(10f);
        }

        var other = plan.Where(i => !Tiers.Any(t => Matches(i, t))).ToList();
        if (other.Count > 0)
        {
            writer.Heading("Other", h3, Accent);
            foreach (var item in other)
            {
                writer.Bullet(Join(item.Priority, item.Chapter, item.Issue), body, Ink);
                if (!string.IsNullOrWhiteSpace(item.SuggestedFix))
                    writer.Paragraph(item.SuggestedFix, new SKFont(EmbeddedTypefaces.SerifItalic, DesignTokens.Pdf.Body), Muted);
            }
        }
    }

    private static void RenderVerdict(
        PageWriter writer, Verdict verdict, SKFont body, SKFont bodyBold, SKFont bodyItalic, SKFont h2)
    {
        if (verdict.Strengths is not { Count: > 0 } && verdict.Problems is not { Count: > 0 } &&
            string.IsNullOrWhiteSpace(verdict.MostNeeds))
            return;

        writer.Heading("The verdict", h2, Ink, ruled: true);
        writer.Paragraph("The candid state of this draft.", bodyItalic, Muted);
        if (verdict.Strengths is { Count: > 0 })
        {
            writer.Paragraph("Biggest strengths:", bodyBold, Ink);
            foreach (var strength in verdict.Strengths)
                writer.Bullet(strength, body, Ink);
        }
        if (verdict.Problems is { Count: > 0 })
        {
            writer.Paragraph("Biggest problems:", bodyBold, Ink);
            foreach (var problem in verdict.Problems)
                writer.Bullet(problem, body, Ink);
        }
        if (!string.IsNullOrWhiteSpace(verdict.MostNeeds))
        {
            writer.Paragraph("What this draft most needs:", bodyBold, Ink);
            writer.Paragraph(verdict.MostNeeds, body, Ink);
        }
    }

    private static void Section(PageWriter writer, string heading, IReadOnlyList<string>? items, SKFont body, SKFont h2)
    {
        if (items is not { Count: > 0 })
            return;
        writer.Heading(heading, h2, Ink, ruled: true);
        foreach (var item in items)
            writer.Bullet(item, body, Ink);
        writer.Gap(18f);
    }

    // A section of findings. Each bullet leads with the chapter it was drawn from, which is the first
    // thing the author needs in order to act on it.
    private static void Section(PageWriter writer, string heading, IReadOnlyList<Finding>? items, SKFont body, SKFont h2)
    {
        if (items is not { Count: > 0 })
            return;
        writer.Heading(heading, h2, Ink, ruled: true);
        foreach (var item in items)
            writer.Bullet(Join(item.Chapter, item.Detail), body, Ink);
        writer.Gap(18f);
    }

    // Joins the parts of one finding, dropping the empty ones: a model that skipped a field — or an
    // older report that never had it (no chapter citation, say) — must not render as a dangling dash.
    private static string Join(params string?[] parts) =>
        string.Join(" — ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string Quoted(string? excerpt) =>
        string.IsNullOrWhiteSpace(excerpt) ? string.Empty : $"“{excerpt}”";

    // Accumulates drawn text and images down a page, snapshotting to PNG and starting a fresh page when the
    // content cursor would overflow the bottom margin. All positions derive from DesignTokens.Pdf so the
    // page geometry stays in one place. Every finished page gets a footer: running title left in muted
    // ink, page number right in ink.
    private sealed class PageWriter
    {
        private const float ContentLeft = DesignTokens.Pdf.SideMargin;
        private const float ContentWidth = DesignTokens.Pdf.PageWidth - 2 * DesignTokens.Pdf.SideMargin;
        private const float ContentTop = DesignTokens.Pdf.TopMargin;
        private const float ContentBottom = DesignTokens.Pdf.PageHeight - DesignTokens.Pdf.BottomMargin;

        private readonly string runningTitle;
        private readonly List<byte[]> pages = [];
        private readonly List<(string Title, int Page)> tocEntries = [];
        private SKSurface surface = null!;
        private SKCanvas canvas = null!;
        private float y;

        public PageWriter(string runningTitle)
        {
            this.runningTitle = runningTitle;
            StartPage();
        }

        /// <summary>H2 sections and the content page (1-based) they begin on.</summary>
        public IReadOnlyList<(string Title, int Page)> TocEntries => tocEntries;

        public void Gap(float height) => y += height;

        // Break to a fresh page if the next `height` points wouldn't fit, so a block (a scene's image +
        // citation) isn't split across a page boundary.
        public void KeepTogether(float height)
        {
            if (y + height > ContentBottom && y > ContentTop)
                NewPage();
        }

        public void Heading(string text, SKFont font, SKColor color, bool ruled = false)
        {
            y += 10f;
            if (y + font.Size * DesignTokens.Pdf.LineSpacing * 2 > ContentBottom)
                NewPage();
            if (font.Size == DesignTokens.Pdf.H2)
                tocEntries.Add((text, pages.Count + 1));
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

        // Draws one wordless evidence image: a portrait block, letterboxed on shaded paper, breaking to a
        // fresh page if it wouldn't fit.
        public void Image(byte[] png)
        {
            float height = DesignTokens.Pdf.SceneImageHeight;
            float width = DesignTokens.Pdf.SceneImageWidth;
            if (y + height > ContentBottom && y > ContentTop)
                NewPage();

            var dest = new SKRect(ContentLeft, y, ContentLeft + width, y + height);
            if (!SkiaImageFit.Draw(canvas, png, dest))
            {
                // Undecodable bytes: leave a shaded slot rather than crashing the whole report.
                using var shade = new SKPaint { Color = SKColor.Parse(DesignTokens.PaperShade), IsAntialias = false };
                canvas.DrawRect(dest, shade);
            }
            y += height + 8f;
        }

        // A compact tension-over-chapters chart: an area under a polyline, so a saggy stretch reads at a
        // glance. Values are clamped 0..1; an empty curve draws nothing.
        public void PacingChart(IReadOnlyList<ChapterTension> curve)
        {
            if (curve is not { Count: > 0 })
                return;

            float height = DesignTokens.Pdf.PacingChartHeight;
            float width = ContentWidth;
            if (y + height > ContentBottom && y > ContentTop)
                NewPage();

            float left = ContentLeft;
            float top = y;
            float bottom = y + height;

            // Baseline + frame in muted ink.
            using (var frame = new SKPaint { Color = Muted, IsAntialias = true, StrokeWidth = 1, Style = SKPaintStyle.Stroke })
                canvas.DrawLine(left, bottom, left + width, bottom, frame);

            var points = new SKPoint[curve.Count];
            float step = curve.Count == 1 ? 0 : width / (curve.Count - 1);
            for (int i = 0; i < curve.Count; i++)
            {
                float px = curve.Count == 1 ? left + width / 2 : left + i * step;
                float t = (float)Math.Clamp(curve[i].Tension, 0, 1);
                float py = bottom - t * (height - 6f);
                points[i] = new SKPoint(px, py);
            }

            // Area fill under the curve, faint accent.
            using (var area = new SKPath())
            {
                area.MoveTo(points[0].X, bottom);
                foreach (var p in points)
                    area.LineTo(p.X, p.Y);
                area.LineTo(points[^1].X, bottom);
                area.Close();
                using var fill = new SKPaint { Color = Accent.WithAlpha(0x33), IsAntialias = true, Style = SKPaintStyle.Fill };
                canvas.DrawPath(area, fill);
            }

            // The tension line + points in accent.
            using (var line = new SKPaint { Color = Accent, IsAntialias = true, StrokeWidth = 2, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round })
            {
                if (points.Length > 1)
                    for (int i = 1; i < points.Length; i++)
                        canvas.DrawLine(points[i - 1], points[i], line);
            }
            using (var dot = new SKPaint { Color = Accent, IsAntialias = true, Style = SKPaintStyle.Fill })
                foreach (var p in points)
                    canvas.DrawCircle(p.X, p.Y, 2.5f, dot);

            y = bottom + 12f;
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
            PaperGrain.Draw(canvas, DesignTokens.Pdf.PageWidth, DesignTokens.Pdf.PageHeight);
            y = ContentTop;
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
