using Folio.Domain.Analysis;
using Folio.Domain.Readiness;
using Folio.Domain.Report;

namespace Folio.Domain.Markup;

/// <summary>
/// Turns the report's findings into marks on the manuscript.
///
/// <para><b>Nothing new is asked of a model.</b> The report already quotes the prose it is talking
/// about — that is what <c>LineEdit.Excerpt</c> and <c>ShowTellNote.Excerpt</c> are for, "quoted so
/// the author can find it" — so a mark is that quotation located in the manuscript. Three of the
/// four marks have a source: a line edit is a <b>query</b>, a passage that runs long is a
/// <b>dele</b>, and a summary that wants to be a scene is a <b>caret</b>, since a caret is the mark
/// for something that belongs here and is not here.</para>
///
/// <para><b>The check has no source, and that is a finding rather than an omission.</b> Nothing in
/// the report quotes prose that is <i>working</i>: the strengths are stated about the book, not
/// about a sentence. So the mark is drawn, tested and part of the vocabulary, and no marked page
/// carries one until something gives it a quotation to sit under.</para>
///
/// <para><b>The suggestion is dropped at this boundary</b>, deliberately and visibly: both source
/// records carry one, <see cref="Mark"/> has nowhere to put it (F40, guarded by
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
