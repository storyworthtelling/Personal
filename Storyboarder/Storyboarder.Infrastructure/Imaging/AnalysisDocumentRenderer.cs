using SkiaSharp;
using Storyboarder.Domain.Analysis;
using Storyboarder.Domain.Design;
using Storyboarder.Domain.Imaging;
using Storyboarder.Domain.Model;
using Storyworthtelling.Paper;

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
    // Colours resolved through the shared Paper palette. The report renders at the Screen profile (its
    // primary targets are the Drive PDF and the in-app view); PaperPalette is now the single source of these
    // hues, so the same document can be re-rendered at the Paper Pro / rM2 profiles when per-destination
    // colour rendering is wired and verified on device.
    private const DeviceColorProfile Profile = DeviceColorProfile.Screen;
    private static readonly SKColor Paper = PaperPalette.Resolve(PaperRole.Ground, Profile);
    private static readonly SKColor Ink = PaperPalette.Resolve(PaperRole.Ink, Profile);
    private static readonly SKColor Accent = PaperPalette.Resolve(PaperRole.Active, Profile);
    private static readonly SKColor Muted = PaperPalette.Resolve(PaperRole.InkMuted, Profile);

    private readonly CoverPageRenderer covers = new();

    public IReadOnlyList<byte[]> Render(
        BookAnalysisResult analysis, string title, IReadOnlyList<SceneEvidence> scenes, DraftReview? review = null) =>
        RenderReport(analysis, title, scenes, review).Pages;

    /// <summary>
    /// Renders content pages first (recording each H2 section's page number),
    /// then prepends unnumbered front matter: cover + table of contents.
    /// Content folios stay 1..N, so the printed TOC needs no bookkeeping.
    /// </summary>
    public AnalysisRenderResult RenderReport(
        BookAnalysisResult analysis, string title, IReadOnlyList<SceneEvidence> scenes, DraftReview? review = null)
    {
        var reportTitle = string.IsNullOrWhiteSpace(title) ? "Book Analysis" : title;
        var content = RenderContent(analysis, reportTitle, scenes, review ?? DraftReview.FirstDraft, out var tocEntries);

        var pages = new List<byte[]> { covers.AnalysisCover(reportTitle) };
        var toc = covers.AnalysisToc(tocEntries);
        pages.AddRange(toc);
        pages.AddRange(content);
        return new AnalysisRenderResult(pages, 1 + toc.Count, tocEntries);
    }

    private IReadOnlyList<byte[]> RenderContent(
        BookAnalysisResult analysis, string reportTitle, IReadOnlyList<SceneEvidence> scenes, DraftReview review,
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

        // Genre + length, framed against the genre norm. The expansion read's length note is preferred when
        // present — it says how far short the draft runs (the owner's headline need) — else the plain frame.
        var lengthNote = analysis.Expansion.Length.Note;
        if (!string.IsNullOrWhiteSpace(analysis.Genre) || analysis.WordCount > 0 || !string.IsNullOrWhiteSpace(lengthNote))
        {
            string lengthText;
            if (!string.IsNullOrWhiteSpace(lengthNote))
                lengthText = lengthNote;
            else
            {
                lengthText = analysis.WordCount > 0 ? $"{analysis.WordCount:N0} words" : string.Empty;
                if (!string.IsNullOrWhiteSpace(analysis.WordCountRange))
                    lengthText = string.IsNullOrEmpty(lengthText)
                        ? $"genre norm {analysis.WordCountRange}"
                        : $"{lengthText} (genre norm {analysis.WordCountRange})";
            }
            writer.Paragraph(Join(analysis.Genre, lengthText), bodyBold, Ink);
        }
        writer.Gap(20f);

        // On a later draft, a top summary of what moved since the previous one, and the list of findings it
        // resolved — the draft-over-draft read the revision loop turns on.
        var statusByFinding = BuildStatusLookup(review);
        if (review.DraftNumber > 1)
        {
            var newCount = review.Findings.Count(f => f.Status == FindingStatus.New);
            var regressedCount = review.Findings.Count(f => f.Status == FindingStatus.Regressed);
            writer.Heading($"Since draft {review.DraftNumber - 1}", h2, Ink, ruled: true);
            writer.Paragraph(
                $"{review.Resolved.Count} resolved · {newCount} new · {regressedCount} regressed. " +
                "New and regressed findings are marked in place below.",
                bodyItalic, Muted);
            foreach (var resolved in review.Resolved)
                writer.Bullet("Resolved — " + Join(resolved.Chapter, resolved.Detail), body, Muted);
            writer.Gap(18f);
        }

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

        // Story integrity — the top analytical priority. On a later draft each finding is marked with its
        // cross-draft status ([new]/[carried]/[regressed]).
        Section(writer, "Plot holes", FindingCategory.PlotHole, analysis.PlotHoles, statusByFinding, body, h2);
        Section(writer, "Inconsistencies", FindingCategory.Inconsistency, analysis.Inconsistencies, statusByFinding, body, h2);
        Section(writer, "Inaccuracies", FindingCategory.Inaccuracy, analysis.Inaccuracies, statusByFinding, body, h2);

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
                writer.Bullet(Join(note.Chapter, note.Detail) + Tag(FindingCategory.Pacing, note.Detail, statusByFinding), body, Ink);
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

        // Show vs tell, both ends of the pendulum: summaries that could become scenes (the additive lever)
        // and over-shown passages to tighten.
        RenderShowVsTell(writer, analysis.Expansion.ShowVsTell, body, bodyBold, bodyItalic, h2);

        RenderVoiceConsistency(writer, analysis.VoiceConsistency, body, bodyBold, bodyItalic, h2);

        // Series continuity (Phase 5): on a sequel, where this book contradicts the prior book's canon and how it
        // handles the prior book's open threads. Silent for a standalone book or a clean sequel.
        RenderSeriesContinuity(writer, analysis.SeriesContinuity, body, bodyBold, bodyItalic, h2);

        RenderReviews(writer, analysis.Reviews, body, bodyBold, bodyItalic, h2, h3);

        // The scene-by-scene walk: each scene's evidence image above its notes — the fidelity check.
        RenderScenes(writer, scenes, body, bodyItalic, h2);

        // What to do next: first the scenes to ADD (the additive engine — the owner writes short), then the
        // revision plan, both grouped by impact tier.
        RenderScenesToAdd(writer, analysis.Expansion.ScenesToAdd, body, bodyItalic, h2, h3);
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

    // The cross-book continuity read (Phase 5): contradictions of the prior book's established canon, and the
    // status of the prior book's carried-over open threads. Rendered only when there is something to say — a
    // standalone book or a clean sequel is silent (quiet by default).
    private static void RenderSeriesContinuity(
        PageWriter writer, SeriesContinuity series, SKFont body, SKFont bodyBold, SKFont bodyItalic, SKFont h2)
    {
        if (!series.HasContent)
            return;

        writer.Heading("Series continuity", h2, Ink, ruled: true);
        var against = string.IsNullOrWhiteSpace(series.PriorBookTitle)
            ? "the previous book"
            : $"“{series.PriorBookTitle}”";
        writer.Paragraph(
            $"How this book holds together with {against} — where it clashes with what was established, and " +
            "what it does with the threads the previous book left open.",
            bodyItalic, Muted);

        if (series.Breaks is { Count: > 0 })
        {
            writer.Paragraph("Contradicts the previous book:", bodyBold, Ink);
            foreach (var brk in series.Breaks)
            {
                writer.Bullet(Join(brk.Chapter, brk.Contradiction), body, Ink);
                if (!string.IsNullOrWhiteSpace(brk.Established))
                    writer.Paragraph($"Previously established: {brk.Established}", bodyItalic, Muted);
                writer.Gap(8f);
            }
        }

        if (series.CarriedThreads is { Count: > 0 })
        {
            writer.Paragraph("Threads carried from the previous book:", bodyBold, Ink);
            foreach (var thread in series.CarriedThreads)
                writer.Bullet(Join(thread.Status, thread.Established), body, Ink);
            writer.Gap(12f);
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

    // The additive engine (Phase 3): scenes and beats to ADD, ranked by the same Must/Should/Consider impact
    // tiers as the revision plan. Pointers only — the intro is explicit that these are the author's to write.
    private static void RenderScenesToAdd(
        PageWriter writer, IReadOnlyList<SceneAddition> additions, SKFont body, SKFont bodyItalic, SKFont h2, SKFont h3)
    {
        if (additions is not { Count: > 0 })
            return;

        writer.Heading("Scenes to add", h2, Ink, ruled: true);
        writer.Paragraph(
            "Where the book could grow — beats that happen off-page, scenes that would deepen a character or " +
            "raise the stakes. These are pointers to write yourself, never drafted scenes.",
            bodyItalic, Muted);

        bool Matches(SceneAddition item, string tier) =>
            string.Equals((item.Priority ?? string.Empty).Trim(), tier, StringComparison.OrdinalIgnoreCase);

        void Render(SceneAddition addition)
        {
            writer.Bullet(Join(KindLabel(addition.Kind), addition.Location, addition.Missing), body, Ink);
            if (!string.IsNullOrWhiteSpace(addition.Why))
                writer.Paragraph(addition.Why, bodyItalic, Muted);
        }

        foreach (var tier in Tiers)
        {
            var items = additions.Where(a => Matches(a, tier)).ToList();
            if (items.Count == 0)
                continue;
            writer.Heading(tier, h3, Accent);
            foreach (var item in items)
                Render(item);
            writer.Gap(10f);
        }

        var other = additions.Where(a => !Tiers.Any(t => Matches(a, t))).ToList();
        if (other.Count > 0)
        {
            writer.Heading("Consider", h3, Accent);
            foreach (var item in other)
                Render(item);
        }
        writer.Gap(18f);
    }

    private static string KindLabel(AdditionKind kind) => kind switch
    {
        AdditionKind.MissingBeat => "Missing beat",
        AdditionKind.CharacterDeepening => "Character",
        AdditionKind.TensionStakes => "Tension & stakes",
        AdditionKind.ThemeDeepening => "Theme",
        _ => "Add"
    };

    // Show vs tell, both ends of the pendulum: passages TOLD in summary that could become dramatized scenes
    // (the main lever for adding words), and OVER-shown passages that run long. Diagnosis only — the excerpt
    // is quoted so the author can find it, the suggestion is a pointer, not a rewrite.
    private static void RenderShowVsTell(
        PageWriter writer, IReadOnlyList<ShowTellNote> notes, SKFont body, SKFont bodyBold, SKFont bodyItalic, SKFont h2)
    {
        if (notes is not { Count: > 0 })
            return;

        writer.Heading("Show vs tell", h2, Ink, ruled: true);

        void RenderGroup(string heading, ShowTellDirection direction)
        {
            var group = notes.Where(n => n.Direction == direction).ToList();
            if (group.Count == 0)
                return;
            writer.Paragraph(heading, bodyBold, Ink);
            foreach (var note in group)
            {
                writer.Bullet(Join(note.Chapter, Quoted(note.Excerpt)), bodyItalic, Ink);
                if (!string.IsNullOrWhiteSpace(note.Suggestion))
                    writer.Paragraph(note.Suggestion, body, Ink);
            }
            writer.Gap(8f);
        }

        RenderGroup("Summarized — could become a scene:", ShowTellDirection.Dramatize);
        RenderGroup("Over-shown — could be tightened:", ShowTellDirection.Tighten);
        writer.Gap(10f);
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
    // thing the author needs in order to act on it, and (on a later draft) trails a cross-draft status tag.
    private static void Section(
        PageWriter writer, string heading, FindingCategory category, IReadOnlyList<ReportFinding>? items,
        IReadOnlyDictionary<(FindingCategory, string), FindingStatus> statusByFinding, SKFont body, SKFont h2)
    {
        if (items is not { Count: > 0 })
            return;
        writer.Heading(heading, h2, Ink, ruled: true);
        foreach (var item in items)
            writer.Bullet(Join(item.Chapter, item.Detail) + Tag(category, item.Detail, statusByFinding), body, Ink);
        writer.Gap(18f);
    }

    // Maps each of this draft's findings to its cross-draft status, keyed by category + normalized detail —
    // the same text the finding rows were built from, so the match back to the report's finding lists is
    // exact. Empty on a first draft (no tags to draw).
    private static IReadOnlyDictionary<(FindingCategory, string), FindingStatus> BuildStatusLookup(DraftReview review)
    {
        var lookup = new Dictionary<(FindingCategory, string), FindingStatus>();
        if (review.DraftNumber <= 1)
            return lookup;
        foreach (var finding in review.Findings)
            lookup[(finding.Category, NormalizeDetail(finding.Detail))] = finding.Status;
        return lookup;
    }

    // The inline status mark for one finding: only New and Regressed are called out (a carried finding needs
    // no mark, and a first draft has no history), so the report highlights what actually moved.
    private static string Tag(
        FindingCategory category, string detail, IReadOnlyDictionary<(FindingCategory, string), FindingStatus> statusByFinding) =>
        statusByFinding.TryGetValue((category, NormalizeDetail(detail)), out var status)
            ? status switch
            {
                FindingStatus.New => "  [new]",
                FindingStatus.Regressed => "  [regressed]",
                _ => string.Empty
            }
            : string.Empty;

    private static string NormalizeDetail(string detail) =>
        string.Join(' ', detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

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
                using var shade = new SKPaint { Color = SKColor.Parse(PaperColorTokens.PaperShade), IsAntialias = false };
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
