using System.Text.RegularExpressions;
using Folio.Domain.Analysis;
using Folio.Domain.Model;
using Folio.Domain.Readiness;
using Folio.Domain.Report;

namespace Folio.Domain.Markup;

/// <summary>
/// Turns the report's findings into marks on the manuscript.
///
/// <para><b>Every finding that is about a place in the text is written on the text.</b> There is no
/// longer a section to list the rest in, so each source follows the same ladder, and each rung is
/// there because the one above it can fail without anything erroring:</para>
/// <list type="number">
/// <item>the passage the finding <b>quoted</b>, found in the manuscript (<see cref="ExcerptIndex"/>);</item>
/// <item>failing that, the <b>opening of the chapter it cited</b>, with the note saying so — "In this
/// chapter" — and keeping the words it tried to quote, because they are the only way left to find the
/// sentence;</item>
/// <item>failing that, <b>unplaced</b>: handed back so the report can print it among the whole-book
/// notes. A finding never simply stops appearing.</item>
/// </list>
///
/// <para>A mark on approximately the right <i>sentence</i> would be worse than none, since the page
/// gives the author no way to tell, which is why nothing here is fuzzy. A mark on the right
/// <i>chapter</i> that says it is only that is a different thing, and a true one.</para>
///
/// <para>Two kinds of note are deliberately <b>not</b> pinned down the ladder. An aggregate
/// observation — "all three sound identical", a rhythm that runs through the whole book — has no
/// passage and no single chapter, so pinning it to the one it happened to be noticed in would be a
/// claim the model never made; it goes straight to unplaced. And a scene beat's verdict is about one
/// scene's close, so without its closing line it is dropped rather than printed as a book-wide
/// note, which it is not.</para>
///
/// <para><b>The suggestion is dropped at this boundary</b>, deliberately and visibly: every source
/// record carries one, <see cref="Mark"/> has nowhere to put it (F40, guarded by
/// <c>NudgeNeverWriteTests</c>), and this is where the author stops being handed a line to use.</para>
/// </summary>
public static partial class MarkSource
{
    /// <summary>The longest stretch of a chapter's opening a fallback mark underlines.</summary>
    private const int HeadLimit = 80;

    /// <summary>Marks only, for callers that have no use for the unplaced remainder.</summary>
    public static IReadOnlyList<Mark> From(string manuscript, BookAnalysisResult analysis) =>
        Build(manuscript, analysis, null).Marks;

    /// <param name="statusTag">
    /// On a later draft, the inline mark a finding carries for what moved since the last one
    /// ("[new]", "[regressed]"). The finding lists used to print it as a bullet suffix; with the
    /// bullets gone it rides on the margin note, so the draft-over-draft read is not lost.
    /// </param>
    public static MarkedReport Build(
        string manuscript, BookAnalysisResult analysis, IReadOnlyList<ChapterSpan>? chapters = null,
        Func<FindingCategory, string, string>? statusTag = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var placer = new Placer(manuscript ?? string.Empty, analysis, chapters ?? []);

        // The report's own findings, in the order the old sections ran.
        foreach (var edit in analysis.LineEdits ?? [])
        {
            placer.Place(
                "Line-level edits", MarkKind.Query, Label(edit.Kind), edit.Excerpt, edit.Reason, edit.Chapter,
                Severity(edit.Chapter, analysis), fallsBackToChapter: true,
                unplaced: Join(Quoted(edit.Excerpt), edit.Reason));
        }

        foreach (var note in analysis.Expansion?.ShowVsTell ?? [])
        {
            var tighten = note.Direction == ShowTellDirection.Tighten;
            placer.Place(
                "Show vs tell", tighten ? MarkKind.Dele : MarkKind.Caret, tighten ? "Runs long" : "Summarized",
                note.Excerpt, note.Suggestion, note.Chapter, Severity(note.Chapter, analysis),
                fallsBackToChapter: true, unplaced: Join(Quoted(note.Excerpt), note.Suggestion));
        }

        // Prose-style notes are line-by-line (a filter word, a tic), so — like a line edit — the
        // report's own quoted "evidence" is what the mark anchors to. A pattern with no example is a
        // habit of the whole book.
        foreach (var note in analysis.ProseNotes ?? [])
        {
            placer.Place(
                "Prose style & voice", MarkKind.Query, "Prose", note.Evidence, note.Fix, note.Chapter,
                Severity(note.Chapter, analysis), fallsBackToChapter: !string.IsNullOrWhiteSpace(note.Evidence),
                unplaced: Join(note.Pattern, Quoted(note.Evidence), note.Fix));
        }

        // A dialogue note anchors only when it quotes a real exchange; an aggregate observation ("all
        // three sound identical") has nothing to point at and is a note about the whole book.
        foreach (var note in analysis.DialogueNotes ?? [])
        {
            placer.Place(
                "Dialogue", MarkKind.Query, "Dialogue", note.Excerpt, note.Fix, note.Chapter,
                Severity(note.Chapter, analysis), fallsBackToChapter: !string.IsNullOrWhiteSpace(note.Excerpt),
                unplaced: Join(note.Speakers, note.Observation, note.Fix));
        }

        // The five finding lists. The three about the story's integrity and the one about its pace
        // were chapter-cited bullets; they carry an optional quotation now, and without one they are
        // written on the opening of their chapter.
        PlaceFindings(placer, analysis, "Plot holes", "Plot hole", analysis.PlotHoles, FindingCategory.PlotHole, statusTag);
        PlaceFindings(placer, analysis, "Inconsistencies", "Inconsistency", analysis.Inconsistencies, FindingCategory.Inconsistency, statusTag);
        PlaceFindings(placer, analysis, "Inaccuracies", "Inaccuracy", analysis.Inaccuracies, FindingCategory.Inaccuracy, statusTag);
        PlaceFindings(placer, analysis, "Pacing", "Pacing", analysis.PacingNotes, FindingCategory.Pacing, statusTag);

        // The pacing, voice and continuity personas' own findings fold into the same marks as their
        // sibling notes; every other persona's findings stay in the whole-book notes, which is the
        // persona's own page.
        foreach (var section in analysis.SectionReads ?? [])
        {
            switch (section.PersonaId)
            {
                case "pacing":
                    PlaceFindings(placer, analysis, "Pacing", "Pacing", section.Findings, FindingCategory.Pacing, statusTag);
                    break;
                case "voice-consistency":
                    PlaceFindings(placer, analysis, "Authorial voice consistency", "Voice drift", section.Findings, null, null);
                    break;
                case "series-continuity":
                    PlaceFindings(placer, analysis, "Series continuity", "Contradicts the previous book", section.Findings, null, null);
                    break;
            }
        }

        foreach (var divergence in analysis.VoiceConsistency?.Divergences ?? [])
        {
            placer.Place(
                "Authorial voice consistency", MarkKind.Query, "Voice drift", divergence.Excerpt,
                divergence.Divergence, divergence.Chapter, Severity(divergence.Chapter, analysis),
                fallsBackToChapter: true, unplaced: Join(divergence.Chapter, Quoted(divergence.Excerpt), divergence.Divergence));
        }

        foreach (var brk in analysis.SeriesContinuity?.Breaks ?? [])
        {
            var reason = Join(brk.Contradiction, string.IsNullOrWhiteSpace(brk.Established)
                ? null
                : $"Previously established: {brk.Established}");
            placer.Place(
                "Series continuity", MarkKind.Query, "Contradicts the previous book", brk.Excerpt, reason,
                brk.Chapter, Severity(brk.Chapter, analysis), fallsBackToChapter: true, unplaced: reason);
        }

        foreach (var addition in analysis.Expansion?.ScenesToAdd ?? [])
        {
            placer.PlaceAddition(addition);
        }

        // A scene beat sits at its own close, not a line inside it — the excerpt is quoted from near
        // the scene's end for exactly that reason. Earning its place is praise (Check, the one mark
        // that had no source before this); doing no work is struck through, the same verdict a
        // passage that runs long already carries.
        foreach (var scene in analysis.SceneBreakdown ?? [])
        {
            var earns = scene.Outcome == SceneVerdict.EarnsItsPlace;
            placer.Place(
                "Scene by scene", earns ? MarkKind.Check : MarkKind.Dele, earns ? "Earns its place" : "Does no work",
                scene.Excerpt, Join(scene.Purpose, scene.Verdict), scene.Chapter, Severity(scene.Chapter, analysis),
                fallsBackToChapter: false, unplaced: null);
        }

        return placer.Finish();
    }

    private static void PlaceFindings(
        Placer placer, BookAnalysisResult analysis, string source, string label,
        IReadOnlyList<ReportFinding>? findings, FindingCategory? category,
        Func<FindingCategory, string, string>? statusTag)
    {
        foreach (var finding in findings ?? [])
        {
            var detail = category is { } known && statusTag is not null
                ? finding.Detail + statusTag(known, finding.Detail)
                : finding.Detail;
            placer.Place(
                source, MarkKind.Query, label, finding.Excerpt, detail, finding.Chapter,
                Severity(finding.Chapter, analysis), fallsBackToChapter: true, unplaced: detail);
        }
    }

    // The margin word for a line edit — specific to what is actually wrong, since a query mark
    // promising "Unclear" on a sentence that is really redundant, or clichéd, or just weak, misnames
    // the note under it. Other (an omitted or unrecognized kind) reads honestly as "an edit" rather
    // than guessing at a diagnosis the model never gave.
    private static string Label(LineEditKind kind) => kind switch
    {
        LineEditKind.FilterWord => "Filter word",
        LineEditKind.Redundant => "Redundant",
        LineEditKind.WeakWord => "Weak word",
        LineEditKind.Cliche => "Cliché",
        LineEditKind.Grammar => "Grammar",
        LineEditKind.Digression => "Digression",
        LineEditKind.Tone => "Tone",
        _ => "Line edit",
    };

    private static string AdditionLabel(AdditionKind kind) => kind switch
    {
        AdditionKind.MissingBeat => "Missing beat",
        AdditionKind.CharacterDeepening => "Character",
        AdditionKind.TensionStakes => "Tension & stakes",
        AdditionKind.ThemeDeepening => "Theme",
        _ => "Add",
    };

    private static string Join(params string?[] parts) =>
        string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string Quoted(string? excerpt) =>
        string.IsNullOrWhiteSpace(excerpt) ? string.Empty : $"“{excerpt.Trim()}”";

    /// <summary>
    /// How much a mark matters, taken from the revision plan rather than invented: a mark inside the
    /// chapters a Must covers is a Must. Neither source record carries a tier of its own, and the
    /// alternative — one hue for every mark — would make the severity colours decorative.
    /// </summary>
    private static ReadinessTier Severity(string? chapter, BookAnalysisResult analysis)
    {
        if (ChapterCitation.Span(chapter) is not { } here)
        {
            return ReadinessTier.Consider;
        }

        var worst = ReadinessTier.Consider;
        foreach (var item in analysis.RevisionPlan ?? [])
        {
            if (ChapterCitation.Span(item.Chapter) is not { } covered
                || covered.From > here.To || here.From > covered.To)
            {
                continue;
            }

            var tier = ChapterCitation.Tier(item.Priority);
            worst = tier < worst ? tier : worst;
        }

        return worst;
    }

    [GeneratedRegex(@"^\s*after\b", RegexOptions.IgnoreCase)]
    private static partial Regex After();

    /// <summary>Holds the manuscript's index and the running result, so each source is one call.</summary>
    private sealed class Placer
    {
        private readonly string manuscript;
        private readonly BookAnalysisResult analysis;
        private readonly IReadOnlyList<ChapterSpan> chapters;
        private readonly ExcerptIndex? index;
        private readonly List<Mark> marks = [];
        private readonly List<UnplacedNote> unplaced = [];

        public Placer(string manuscript, BookAnalysisResult analysis, IReadOnlyList<ChapterSpan> chapters)
        {
            this.manuscript = manuscript;
            this.analysis = analysis;
            this.chapters = chapters;
            index = string.IsNullOrEmpty(manuscript) ? null : ExcerptIndex.Of(manuscript);
        }

        public void Place(
            string source, MarkKind kind, string label, string? excerpt, string? reason, string? chapter,
            ReadinessTier severity, bool fallsBackToChapter, string? unplaced)
        {
            if (index?.Find(excerpt) is { } span)
            {
                // A caret is a point, not a run: it says something belongs *here*, at the head of the
                // passage that only summarises it. Drawn over the whole quotation it appears once per
                // line of it, which reads as several missing things rather than one.
                var end = kind == MarkKind.Caret ? span.Start : span.End;
                marks.Add(new Mark(kind, severity, span.Start, end, label, reason ?? string.Empty, source));
                return;
            }

            if (fallsBackToChapter && ChapterHead(chapter) is { } head)
            {
                // Always a query: a dele would strike through the chapter's heading, and a caret there
                // would claim something is missing at a point nobody identified.
                var where = string.IsNullOrWhiteSpace(excerpt)
                    ? "In this chapter —"
                    : $"In this chapter, {Quoted(excerpt)} —";
                marks.Add(new Mark(
                    MarkKind.Query, severity, head.Start, head.End, label, Join(where, reason), source));
                return;
            }

            if (unplaced is not null && !string.IsNullOrWhiteSpace(unplaced))
            {
                this.unplaced.Add(new UnplacedNote(source, chapter ?? string.Empty, unplaced));
            }
        }

        public void PlaceAddition(SceneAddition addition)
        {
            var label = AdditionLabel(addition.Kind);
            var reason = Join(addition.Missing, addition.Why);
            var severity = ChapterCitation.Tier(addition.Priority);

            if (index?.Find(addition.Excerpt) is { } span)
            {
                marks.Add(new Mark(MarkKind.Caret, severity, span.Start, span.Start, label, reason, "Scenes to add"));
                return;
            }

            if (ChapterOf(addition.Location) is { } chapter)
            {
                // "After Chapter 7" is the gap between two chapters, so the caret sits where 7 ends;
                // a bare "Chapter 4" is at its head.
                var at = After().IsMatch(addition.Location ?? string.Empty)
                    ? Math.Min(chapter.EndOffset, manuscript.Length)
                    : Math.Min(chapter.StartOffset, manuscript.Length);
                marks.Add(new Mark(MarkKind.Caret, severity, at, at, label, reason, "Scenes to add"));
                return;
            }

            unplaced.Add(new UnplacedNote(
                "Scenes to add", addition.Location ?? string.Empty,
                Join(string.IsNullOrWhiteSpace(addition.Location) ? null : $"{addition.Location}:", reason)));
        }

        public MarkedReport Finish() =>
            new([.. marks.OrderBy(mark => mark.StartOffset)], [.. unplaced]);

        private ChapterSpan? ChapterOf(string? citation) =>
            index is not null
            && ChapterCitation.Span(citation) is { } cited
            && chapters.Any(candidate => candidate.Number == cited.From)
                ? chapters.First(candidate => candidate.Number == cited.From)
                : null;

        /// <summary>
        /// The first line of a chapter, as a run to underline. A chapter that is not in the list is
        /// not guessed at — a mark on chapter 20 for a problem the reader found in chapter 40 is
        /// indistinguishable on the page from a real one.
        /// </summary>
        private TextRange? ChapterHead(string? citation)
        {
            if (ChapterOf(citation) is not { } chapter)
            {
                return null;
            }

            var limit = Math.Min(chapter.EndOffset, manuscript.Length);
            var start = Math.Clamp(chapter.StartOffset, 0, limit);
            while (start < limit && char.IsWhiteSpace(manuscript[start]))
            {
                start++;
            }

            var end = start;
            while (end < limit && end - start < HeadLimit && manuscript[end] != '\n')
            {
                end++;
            }

            return end > start ? new TextRange(start, end) : null;
        }
    }
}
