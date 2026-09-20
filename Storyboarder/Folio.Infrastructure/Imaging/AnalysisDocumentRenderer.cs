using SkiaSharp;
using Folio.Domain.Analysis;
using Folio.Domain.Markup;
using Folio.Domain.Imaging;
using Folio.Domain.Model;
using Folio.Domain.Reading;
using Folio.Domain.Report;
using Storyworthtelling.Paper;

namespace Folio.Infrastructure.Imaging;

// Renders the revision report — the star product — as a vector PDF with SkiaSharp. The report is
// layered: the analytical/thematic sections first, then the scene-by-scene walk where each scene's
// wordless evidence image sits above its notes (the fidelity check), then what-to-do (the revision
// plan by impact tier), and the candid verdict last. Styling follows DESIGN.md §7.
public sealed record AnalysisRenderResult(
    byte[] Pdf,
    int FrontMatterCount,
    IReadOnlyList<(string Title, int Page)> TocEntries);

public sealed class AnalysisDocumentRenderer
{
    // Colour comes from the report's own palette, which resolves through the Document (muted) profile.
    // These four names are what the content code below has always called them; only what stands behind
    // them changed, which is the point of the release — the page moves, the report does not.
    private static readonly SKColor Ink = ReportPalette.Ink;
    private static readonly SKColor Accent = ReportPalette.Rule;
    private static readonly SKColor Muted = ReportPalette.InkMuted;

    private readonly CoverPageRenderer covers = new();

    public byte[] Render(
        BookAnalysisResult analysis, ReportCover cover, IReadOnlyList<SceneEvidence> scenes,
        DraftReview? review = null, string? manuscript = null) =>
        RenderReport(analysis, cover, scenes, review, manuscript).Pdf;

    /// <summary>
    /// Renders three times, and each pass buys something the next one needs.
    ///
    /// <para>The contents has to name the page each section starts on, and it is printed
    /// <b>before</b> the content it indexes. While every page was a PNG in a list that was free — the
    /// front matter was prepended afterwards. A PDF sink writes as it goes and there is no going back,
    /// so: the first pass renders the content to learn which sections land where, relative to the
    /// content's own first sheet; the second renders the front matter with those entries to learn how
    /// many sheets the front matter itself takes; and the third prints the real document, with the
    /// entries shifted by that count so the contents names the folio a reader will actually see.</para>
    ///
    /// <para>The second pass is needed because the front matter's length depends on the number of
    /// entries, and the entries' numbers depend on the front matter's length. Only the <i>count</i> is
    /// circular, and it settles at once: a contents page holds the same number of lines whether it is
    /// printing two-digit folios or three. All three passes are deterministic, and drawing a hundred
    /// pages of vector type is nothing beside the model calls that wrote the words on them.</para>
    /// </summary>
    public AnalysisRenderResult RenderReport(
        BookAnalysisResult analysis, ReportCover cover, IReadOnlyList<SceneEvidence> scenes,
        DraftReview? review = null, string? manuscript = null)
    {
        var reportTitle = string.IsNullOrWhiteSpace(cover.Title) ? "Book Analysis" : cover.Title;
        var titled = cover with { Title = reportTitle };
        var foot = $"{reportTitle} · draft {titled.DraftNumber}";
        var draft = review ?? DraftReview.FirstDraft;

        IReadOnlyList<(string Title, int Page)> relative;
        using (var probe = new PageWriter(foot))
        {
            RenderContent(analysis, titled, scenes, draft, probe, manuscript);
            relative = probe.TocEntries.ToList();
            probe.Finish();
        }

        int frontMatter;
        using (var probe = new PageWriter(foot))
        {
            covers.AnalysisCover(probe, titled);
            covers.AnalysisToc(probe, relative);
            frontMatter = probe.PageCount;
            probe.Finish();
        }

        var entries = relative.Select(entry => (entry.Title, entry.Page + frontMatter)).ToList();

        using var document = new PageWriter(foot);
        covers.AnalysisCover(document, titled);
        covers.AnalysisToc(document, entries);
        document.NewPage();
        RenderContent(analysis, titled, scenes, draft, document, manuscript);
        return new AnalysisRenderResult(document.Finish(), frontMatter, entries);
    }

    private static void RenderContent(
        BookAnalysisResult analysis, ReportCover cover, IReadOnlyList<SceneEvidence> scenes,
        DraftReview review, PageWriter writer, string? manuscript)
    {
        using var body = ReportType.Body();
        using var bodyBold = ReportType.BodyBold();
        using var bodyItalic = ReportType.BodyItalic();
        using var h1 = ReportType.Title();
        using var h2 = ReportType.Section();
        using var h3 = ReportType.Heading();

        writer.Heading("The frame", h1, Ink, ruled: true);
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
        writer.Gap(PageGeometry.SectionGap);

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
            writer.Gap(PageGeometry.SectionGap);
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
                writer.Gap(PageGeometry.BlockGap);
            }
        }

        // Story integrity — the top analytical priority. On a later draft each finding is marked with its
        // cross-draft status ([new]/[carried]/[regressed]).
        Section(writer, "Plot holes", FindingCategory.PlotHole, analysis.PlotHoles, statusByFinding, body, h2);
        Section(writer, "Inconsistencies", FindingCategory.Inconsistency, analysis.Inconsistencies, statusByFinding, body, h2);
        Section(writer, "Inaccuracies", FindingCategory.Inaccuracy, analysis.Inaccuracies, statusByFinding, body, h2);

        Section(writer, "Themes", analysis.Themes, body, h2);

        // Pacing, with a tension-over-chapters chart when the model returned a curve of at least two
        // points. A single chapter has nothing to trend between it and — a real manuscript proved
        // this: the chart degenerated to one point floating above a bare rule, with nothing on the
        // page saying what either mark was.
        if (analysis.PacingNotes is { Count: > 0 } || analysis.PacingCurve is { Count: > 0 })
        {
            writer.Heading("Pacing", h2, Ink, ruled: true);
            if (analysis.PacingCurve is { Count: >= 2 })
            {
                writer.Paragraph("Tension across the chapters — a dip is a place to check the story is still pulling.", bodyItalic, Muted);
                writer.PacingChart(analysis.PacingCurve, PlacedFindings(analysis));
            }
            foreach (var note in analysis.PacingNotes ?? [])
                writer.Bullet(Join(note.Chapter, note.Detail) + Tag(FindingCategory.Pacing, note.Detail, statusByFinding), body, Ink);
            writer.Gap(PageGeometry.SectionGap);
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
            writer.Gap(PageGeometry.SectionGap);
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
            writer.Gap(PageGeometry.SectionGap);
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
                writer.Gap(PageGeometry.TightGap);
            }
        }

        // Show vs tell, both ends of the pendulum: summaries that could become scenes (the additive lever)
        // and over-shown passages to tighten.
        RenderShowVsTell(writer, analysis.Expansion.ShowVsTell, body, bodyBold, bodyItalic, h2);

        RenderVoiceConsistency(writer, analysis.VoiceConsistency, body, bodyBold, bodyItalic, h2);

        // Series continuity (Phase 5): on a sequel, where this book contradicts the prior book's canon and how it
        // handles the prior book's open threads. Silent for a standalone book or a clean sequel.
        RenderSeriesContinuity(writer, analysis.SeriesContinuity, body, bodyBold, bodyItalic, h2);

        // The manuscript is the centre of the report (P10): everything anchored to a place in the
        // text lives here, before the whole-book sections above give way to the six whole-book
        // voices below. The scene-by-scene walk follows it — each scene's evidence image above its
        // notes, the fidelity check — and only then do the readers speak.
        RenderMarkedManuscript(writer, analysis, manuscript, bodyItalic, h2);
        RenderScenes(writer, scenes, body, bodyItalic, h2);

        RenderReviews(writer, analysis.Reviews, body, bodyBold, bodyItalic, h2, h3);

        // What to do next: first the scenes to ADD (the additive engine — the owner writes short), then the
        // revision plan, both grouped by impact tier.
        RenderScenesToAdd(writer, analysis.Expansion.ScenesToAdd, body, bodyItalic, h2, h3);
        RenderRevisionPlan(writer, analysis.RevisionPlan, body, h2, h3);

        // The candid state of the draft, last.
        RenderVerdict(writer, analysis.Verdict, cover, body, bodyBold, bodyItalic, h2);

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
            writer.KeepTogether(PageGeometry.SceneImageHeight + 80f);
            writer.Paragraph(string.IsNullOrWhiteSpace(scene.Citation) ? "Scene" : scene.Citation, bodyItalic, Accent);
            if (scene.ShowsPair)
                writer.ScenePair(scene.PreviousImage!, scene.Image!);
            else if (scene.Image is { Length: > 0 })
                writer.Image(scene.Image);
            else
                writer.Paragraph("Image unavailable for this scene.", bodyItalic, Muted);
            if (!string.IsNullOrWhiteSpace(scene.Purpose))
                writer.Paragraph(scene.Purpose, body, Ink);
            if (!string.IsNullOrWhiteSpace(scene.Verdict))
                writer.Paragraph(scene.Verdict, bodyItalic, Muted);
            writer.Gap(PageGeometry.SectionGap);
        }
    }

    /// <summary>
    /// The manuscript itself, with the report's findings written on it — the document the author
    /// actually revises from. It is last because it is most of the sheets (F45), and it is skipped
    /// entirely when there are no marks: a hundred and forty pages of unmarked prose is a copy of
    /// the book, not a reading of it.
    /// </summary>
    private static void RenderMarkedManuscript(
        PageWriter writer, BookAnalysisResult analysis, string? manuscript, SKFont bodyItalic, SKFont h2)
    {
        if (string.IsNullOrWhiteSpace(manuscript))
            return;

        // A real .docx paragraph opened with a literal Word tab has no glyph on the page — see
        // ManuscriptText. Sanitized once, before marks are indexed, so the marks and the drawn
        // lines agree on the same offsets throughout.
        manuscript = ManuscriptText.ForPrint(manuscript);

        var marks = MarkSource.From(manuscript, analysis);
        if (marks.Count == 0)
            return;

        writer.Heading("The manuscript, marked", h2, Ink, ruled: true);
        writer.Paragraph(
            "Your pages with the findings on them. A caret is something missing, a struck line is "
            + "something doing no work, an underline is something unclear — and the note in the "
            + "margin says why. None of them writes the line for you.",
            bodyItalic, Muted);

        MarkedManuscript.Render(writer, manuscript, marks);

        // Whatever follows starts on its own sheet. After a hundred and forty pages of marked prose,
        // a section heading four lines under the last one reads as part of the book.
        writer.NewPage();
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
                writer.Gap(PageGeometry.TightGap);
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
                writer.Gap(PageGeometry.TightGap);
            }
        }

        if (series.CarriedThreads is { Count: > 0 })
        {
            writer.Paragraph("Threads carried from the previous book:", bodyBold, Ink);
            foreach (var thread in series.CarriedThreads)
                writer.Bullet(Join(thread.Status, thread.Established), body, Ink);
            writer.Gap(PageGeometry.BlockGap);
        }
    }

    /// <summary>
    /// The six readers: a contents page that maps them, then one page each.
    ///
    /// <para>They used to be seven blocks flowed into a single "Reviews" section, which is how a
    /// long read becomes a paragraph nobody finishes. Each read is a whole-book judgement in
    /// character and gets a sheet of its own, and the sheet before them is the map: who read it,
    /// from where, their verdict in a clause, and the page to turn to.</para>
    /// </summary>
    private static void RenderReviews(
        PageWriter writer, IReadOnlyList<Review> reviews, SKFont body, SKFont bodyBold, SKFont bodyItalic,
        SKFont h2, SKFont h3)
    {
        if (reviews is not { Count: > 0 })
            return;

        RenderReadersContents(writer, reviews, body);

        for (var i = 0; i < reviews.Count; i++)
        {
            RenderReader(writer, reviews[i], i + 1, reviews.Count, body, h3);
        }
    }

    /// <summary>
    /// The readers' contents page: the map to the six sheets that follow.
    ///
    /// <para>The closing line <b>counts</b> the agreement rather than asserting it, and names the
    /// dissent. A reader who wants nothing changed is information, not a rounding error — and a
    /// sentence claiming the readers agree, on a page where one of them plainly does not, is the
    /// kind of summary that teaches an author to stop believing the document.</para>
    /// </summary>
    private static void RenderReadersContents(
        PageWriter writer, IReadOnlyList<Review> reviews, SKFont body)
    {
        writer.NewPage();
        writer.PageLabel($"{Spell(reviews.Count)} readers");
        writer.Paragraph(
            $"{Capitalize(Spell(reviews.Count))} full reviews follow, one to a page. Each read the whole book in "
            + "character and none of them saw the others' notes, so where they agree the note is "
            + "loud, and where they split the split is itself the finding.",
            body, Ink);
        writer.Gap(PageGeometry.BlockGap);

        // The folio each reader's own page will carry. The pages follow this one in order, and this
        // page is itself one sheet, so the first reader is the next folio along.
        var firstReaderFolio = writer.Folio + 1;
        for (var i = 0; i < reviews.Count; i++)
        {
            var review = reviews[i];
            writer.ContentsRow(
                i + 1, PersonaName(review.PersonaId), Lens(review.PersonaId), review.Verdict,
                firstReaderFolio + i);
            if (i < reviews.Count - 1)
                writer.Hairline();
        }

        // The drawing closes this page with a counted consensus — "five of the six name the middle
        // … one asks for no change at all". It is NOT built here, and deliberately: the only count
        // this report can actually take is how many reads carry a weakness, and G23 makes that
        // always all of them. A line that can only ever say "six of the six" is not a count, and
        // the drawn claim — which reads are naming the SAME thing — is a judgement about text that
        // belongs to the model that read them. Raised for the owner rather than substituted.
    }

    /// <summary>
    /// One reader, one page: the long review in character, that reader's own pros and cons, and the
    /// condition on their verdict pinned to the foot.
    ///
    /// <para>The same three parts and the same room whether the read is warm or brutal. A persona
    /// that only prints at length when it is negative is a different instrument, and the author
    /// would learn to skip it.</para>
    /// </summary>
    private static void RenderReader(
        PageWriter writer, Review review, int index, int of, SKFont body, SKFont h3)
    {
        writer.NewPage();
        writer.PageLabel($"Reader {index} of {Spell(of)}", Lens(review.PersonaId));

        writer.Heading(PersonaName(review.PersonaId), h3, Ink);
        foreach (var paragraph in Paragraphs(review.Assessment))
        {
            writer.Paragraph(paragraph, body, Ink);
            writer.Gap(PageGeometry.TightGap);
        }

        if (review.Strengths is { Count: > 0 } || review.Weaknesses is { Count: > 0 })
        {
            writer.Hairline();
            writer.ProsAndCons(
                "What works, for me", review.Strengths ?? [],
                "What does not", review.Weaknesses ?? []);
        }

        if (!string.IsNullOrWhiteSpace(review.Condition))
            writer.Footer("Would need", review.Condition, body, Ink);
    }

    /// <summary>
    /// The reader's question, as the catalogue words it — "Could I sell it", "What is wrong with
    /// it". It is the other half of the page's own label, and it is what makes six reads read as
    /// six questions rather than six opinions.
    /// </summary>
    private static string Lens(string personaId) =>
        PersonaCatalogue.All.FirstOrDefault(persona => persona.Id == personaId)?.Question ?? string.Empty;

    /// <summary>
    /// A long review as the paragraphs the model wrote it in. Blank lines are the break; a read that
    /// arrives as one block prints as one block rather than being cut up by a rule of this file's
    /// own invention.
    /// </summary>
    private static IReadOnlyList<string> Paragraphs(string text) =>
        [.. (text ?? string.Empty)
            .Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(paragraph => paragraph.Replace("\r\n", " ").Replace("\n", " ").Trim())
            .Where(paragraph => paragraph.Length > 0)];

    private static string Spell(int count) => Folio.Domain.Shelf.BookLines.Spell(count);

    private static string Capitalize(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    // A read cites a persona by id; the page prints the persona's name. An id with no persona behind
    // it prints as itself rather than as "Review", so a wrong citation is visible instead of tidy.
    private static string PersonaName(string personaId)
    {
        if (string.IsNullOrWhiteSpace(personaId))
            return "Review";
        return PersonaCatalogue.All.FirstOrDefault(persona => persona.Id == personaId)?.Name ?? personaId;
    }

    // The additive engine (Phase 3): scenes and beats to ADD, ranked by the same Must/Should/Consider impact
    // tiers as the revision plan. Pointers only — the intro is explicit that these are the author's to write.
    // The impact tiers, in the order the plan and the additive engine both group by.
    private static readonly string[] Tiers = ["Must", "Should", "Consider"];

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
            writer.Gap(PageGeometry.BlockGap);
        }

        var other = additions.Where(a => !Tiers.Any(t => Matches(a, t))).ToList();
        if (other.Count > 0)
        {
            writer.Heading("Consider", h3, Accent);
            foreach (var item in other)
                Render(item);
        }
        writer.Gap(PageGeometry.SectionGap);
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
            writer.Gap(PageGeometry.TightGap);
        }

        RenderGroup("Summarized — could become a scene:", ShowTellDirection.Dramatize);
        RenderGroup("Over-shown — could be tightened:", ShowTellDirection.Tighten);
        writer.Gap(PageGeometry.BlockGap);
    }

    /// <summary>
    /// The revision plan: the only page in the report that is a <b>list of actions</b>.
    ///
    /// <para>It opens with the tally in numerals, because three Musts is a draft in good shape and
    /// saying so in figures is less flattering and more useful than saying so in a sentence. Each
    /// item gets an empty check box — this is the page that gets carried to the desk — and the
    /// severity chip sits at the right rather than leading the line, so the tier stays readable down
    /// the page while the order belongs to <i>where the work is</i>. The author makes one pass
    /// through the manuscript, not three.</para>
    /// </summary>
    private static void RenderRevisionPlan(
        PageWriter writer, IReadOnlyList<RevisionItem> plan, SKFont body, SKFont h2, SKFont h3)
    {
        if (plan is not { Count: > 0 })
            return;

        writer.NewPage();
        writer.PageLabel("What to do about it");

        writer.Tally(
            [.. Tiers.Select(tier => (plan.Count(item => Matches(item, tier)), tier))],
            "Ordered by where the work is, not by severity — one pass through the manuscript does all of it.");

        foreach (var tier in Tiers)
        {
            var items = plan.Where(item => Matches(item, tier)).ToList();
            if (items.Count == 0)
                continue;

            writer.Hairline();
            writer.Heading(tier, h3, Ink);
            writer.Continues(tier);
            foreach (var item in items)
                writer.CheckItem(Sentences(item.Issue, item.SuggestedFix), item.Chapter, Chip(tier));
        }

        var other = plan.Where(item => !Tiers.Any(tier => Matches(item, tier))).ToList();
        if (other.Count > 0)
        {
            writer.Hairline();
            writer.Heading("Other", h3, Ink);
            writer.Continues("Other");
            foreach (var item in other)
                writer.CheckItem(
                    Sentences(item.Issue, item.SuggestedFix), Join(item.Priority, item.Chapter),
                    Chip("Consider"));
        }

        writer.Continues(null);

        using var small = ReportType.Small();
        writer.Footer(
            null,
            "Nothing on this list is a sentence to copy — each item says what to do and where, "
            + "and leaves the writing to the author.",
            small, Muted);
    }

    /// <summary>
    /// What to do, and then why — run together as prose rather than joined with the report's
    /// citation dash. Both halves are sentences; a dash between two sentences reads as a citation
    /// separator, which is what it means everywhere else in this document.
    /// </summary>
    private static string Sentences(params string?[] parts) =>
        string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

    private static bool Matches(RevisionItem item, string tier) =>
        string.Equals((item.Priority ?? string.Empty).Trim(), tier, StringComparison.OrdinalIgnoreCase);

    private static SKColor Chip(string tier) => tier.Trim().ToLowerInvariant() switch
    {
        "must" => ReportPalette.MustFix,
        "should" => ReportPalette.Should,
        _ => ReportPalette.Consider,
    };

    /// <summary>
    /// The verdict, and the last page.
    ///
    /// <para><b>Strengths come before problems by decision, not by politeness</b> (G21). A report
    /// whose first page of judgement is a list of faults gets read defensively, and this one is
    /// asking for a specific piece of work. The closing line names what the draft most needs in a
    /// single action: if the verdict cannot say that in one sentence, the sections above it have not
    /// finished their job.</para>
    /// </summary>
    private static void RenderVerdict(
        PageWriter writer, Verdict verdict, ReportCover cover,
        SKFont body, SKFont bodyBold, SKFont bodyItalic, SKFont h2)
    {
        if (verdict.Strengths is not { Count: > 0 } && verdict.Problems is not { Count: > 0 }
            && string.IsNullOrWhiteSpace(verdict.MostNeeds)
            && string.IsNullOrWhiteSpace(verdict.Standing))
            return;

        writer.NewPage();
        writer.PageLabel("Verdict");

        if (!string.IsNullOrWhiteSpace(verdict.Standing))
        {
            using var h3 = ReportType.Heading();
            writer.Heading(verdict.Standing, h3, Ink);
        }
        if (!string.IsNullOrWhiteSpace(verdict.Reading))
        {
            writer.Paragraph(verdict.Reading, body, Ink);
            writer.Gap(PageGeometry.TightGap);
        }

        Judgement(writer, "Biggest strengths", verdict.Strengths, body, bodyBold);
        Judgement(writer, "Biggest problems", verdict.Problems, body, bodyBold);

        if (!string.IsNullOrWhiteSpace(verdict.MostNeeds))
        {
            writer.Hairline();
            writer.Label("What this draft most needs", PageGeometry.ContentLeft, writer.Y + PaperTypeScale.Label);
            writer.Y += PaperTypeScale.Label + 5f;
            writer.Paragraph(verdict.MostNeeds, body, Ink);
        }

        Colophon(writer, cover);
    }

    private static void Judgement(
        PageWriter writer, string label, IReadOnlyList<string>? items, SKFont body, SKFont bodyBold)
    {
        if (items is not { Count: > 0 })
            return;

        writer.Hairline();
        writer.Label(label, PageGeometry.ContentLeft, writer.Y + PaperTypeScale.Label);
        writer.Y += PaperTypeScale.Label + 5f;
        foreach (var item in items)
        {
            writer.Paragraph(item, body, Ink);
            writer.Gap(PageGeometry.TightGap);
        }
    }

    /// <summary>
    /// What was read, how long it was, and who it was prepared for — the line that closes the
    /// document, set at the foot where a colophon belongs.
    /// </summary>
    private static void Colophon(PageWriter writer, ReportCover cover)
    {
        using var small = ReportType.Small();
        var read = cover.ReadOn.ToLocalTime().ToString("d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
        var length = cover.WordCount > 0 ? $" · {cover.WordCount:N0} words" : string.Empty;
        writer.Footer(null, $"Read {read}{length} · draft {Spell(cover.DraftNumber)}", small, Muted);

        // Who it is for, set against the right margin on the same line — the closing half of a
        // colophon, and the only place in the document that names its reader.
        // Back up over the line just written AND the gap Paragraph leaves after it, so the two
        // halves sit on one baseline rather than on two four points apart.
        writer.Y -= (small.Size * PageGeometry.LineSpacing) + PageGeometry.TightGap;
        writer.Right("Prepared for the author", small, Muted);
    }

    private static void Section(PageWriter writer, string heading, IReadOnlyList<string>? items, SKFont body, SKFont h2)
    {
        if (items is not { Count: > 0 })
            return;
        writer.Heading(heading, h2, Ink, ruled: true);
        foreach (var item in items)
            writer.Bullet(item, body, Ink);
        writer.Gap(PageGeometry.SectionGap);
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
        writer.Gap(PageGeometry.SectionGap);
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

    /// <summary>
    /// The revision plan, placed on the pacing chart (F39). A finding whose citation names no chapter
    /// on the curve is simply not drawn: the chart says less rather than saying it in the wrong
    /// place. See <see cref="ChapterCitation"/>.
    /// </summary>
    private static IReadOnlyList<PacingFinding> PlacedFindings(BookAnalysisResult analysis) =>
    [
        .. (analysis.RevisionPlan ?? [])
            .Select(item => (Item: item, Range: ChapterCitation.Locate(item.Chapter, analysis.PacingCurve)))
            .Where(placed => placed.Range is not null)
            .Select(placed => new PacingFinding(
                placed.Range!.Value.From, placed.Range.Value.To, ChapterCitation.Tier(placed.Item.Priority))),
    ];
}
