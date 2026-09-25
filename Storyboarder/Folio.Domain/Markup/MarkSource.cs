using Folio.Domain.Analysis;
using Folio.Domain.Readiness;
using Folio.Domain.Report;

namespace Folio.Domain.Markup;

/// <summary>
/// Turns the report's findings into marks on the manuscript.
///
/// <para><b>Nothing new is asked of a model that wasn't already quoting the prose it's talking
/// about.</b> A line edit, a prose note, and a dialogue note with a real exchange are all
/// <b>queries</b>; a show-vs-tell passage that runs long, or a scene that does no work, is a
/// <b>dele</b>; a summary that wants to be a scene is a <b>caret</b>, since a caret is the mark for
/// something that belongs here and is not here; and a scene that earns its place is a
/// <b>check</b> — the first real source for it. Before scene beats carried an outcome, nothing in
/// the report quoted prose that was <i>working</i> (the strengths were stated about the book, not a
/// sentence), so the check sat in the vocabulary, drawn and tested, with no page ever carrying one.</para>
///
/// <para>Not every source record can anchor: a dialogue note about an aggregate pattern ("all three
/// sound identical") or a scene beat the model gave no closing quotation for has nothing to point
/// at, and produces no mark rather than a guessed one.</para>
///
/// <para><b>The suggestion is dropped at this boundary</b>, deliberately and visibly: every source
/// record carries one, <see cref="Mark"/> has nowhere to put it (F40, guarded by
/// <c>NudgeNeverWriteTests</c>), and this is where the author stops being handed a line to use.</para>
/// </summary>
public static class MarkSource
{
    public static IReadOnlyList<Mark> From(string manuscript, BookAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        if (string.IsNullOrEmpty(manuscript))
        {
            return [];
        }

        var index = ExcerptIndex.Of(manuscript);
        var marks = new List<Mark>();

        foreach (var edit in analysis.LineEdits ?? [])
        {
            Add(marks, index, edit.Excerpt, MarkKind.Query, Label(edit.Kind), edit.Reason, edit.Chapter, analysis);
        }

        foreach (var note in analysis.Expansion?.ShowVsTell ?? [])
        {
            var kind = note.Direction == ShowTellDirection.Tighten ? MarkKind.Dele : MarkKind.Caret;
            var label = note.Direction == ShowTellDirection.Tighten ? "Runs long" : "Summarized";
            Add(marks, index, note.Excerpt, kind, label, note.Suggestion, note.Chapter, analysis);
        }

        // Prose-style notes are line-by-line (a filter word, a tic), so — like a line edit — the
        // report's own quoted "evidence" is what the mark anchors to.
        foreach (var note in analysis.ProseNotes ?? [])
        {
            Add(marks, index, note.Evidence, MarkKind.Query, "Prose", note.Fix, note.Chapter, analysis);
        }

        // A dialogue note anchors only when it quotes a real exchange; an aggregate observation ("all
        // three sound identical") has nothing to point at and stays a bullet under its own section.
        foreach (var note in analysis.DialogueNotes ?? [])
        {
            Add(marks, index, note.Excerpt, MarkKind.Query, "Dialogue", note.Fix, note.Chapter, analysis);
        }

        // A scene beat sits at its own close, not a line inside it — the excerpt is quoted from near
        // the scene's end for exactly that reason. Earning its place is praise (Check, the one mark
        // that had no source before this); doing no work is struck through, the same verdict a
        // passage that runs long already carries.
        foreach (var scene in analysis.SceneBreakdown ?? [])
        {
            var kind = scene.Outcome == SceneVerdict.EarnsItsPlace ? MarkKind.Check : MarkKind.Dele;
            var label = scene.Outcome == SceneVerdict.EarnsItsPlace ? "Earns its place" : "Does no work";
            Add(marks, index, scene.Excerpt, kind, label, Join(scene.Purpose, scene.Verdict), scene.Chapter, analysis);
        }

        return [.. marks.OrderBy(mark => mark.StartOffset)];
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

    private static string Join(params string?[] parts) =>
        string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static void Add(
        List<Mark> marks, ExcerptIndex index, string? excerpt, MarkKind kind,
        string label, string? reason, string? chapter, BookAnalysisResult analysis)
    {
        if (index.Find(excerpt) is not { } span)
        {
            return;   // the quotation is not in this manuscript: no mark rather than a guessed one
        }

        // A caret is a point, not a range: it says something belongs *here*, at the head of the
        // passage that only summarises it. Drawn over the whole quotation it appears once per line
        // of it, which reads as several missing things rather than one.
        var end = kind == MarkKind.Caret ? span.Start : span.End;

        marks.Add(new Mark(
            kind, Severity(chapter, analysis), span.Start, end,
            label, reason ?? string.Empty, Provenance: kind.ToString()));
    }

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
}
