using System.Text.RegularExpressions;

namespace Folio.Domain.Text;

// One detected scene, spanning [StartOffset, EndOffset) of the source text. Number is 1-based reading
// order across the whole manuscript; ChapterNumber/ChapterLabel cite where it sits so the report can
// reference it as "Ch. N · Scene M".
public sealed record SceneSpan(int Number, int ChapterNumber, string ChapterLabel, int StartOffset, int EndOffset);

// Splits a manuscript into scenes — the atomic unit of the review, each carrying one wordless evidence
// image. Pure and deterministic: the same text + chapters always yield the same scenes, so detection runs
// once at ingestion and is trusted thereafter.
//
// Detection is HYBRID:
//   1. Honor AUTHORIAL scene breaks within each chapter — a divider line ("* * *", "---", "#") or a gap
//      of two or more blank lines.
//   2. For a long chapter with no authorial breaks (or a scene that still runs very long), fall back to
//      splitting at paragraph boundaries near a target length, so a wall-of-text chapter still yields
//      several scenes instead of one enormous one. (A Claude-assisted refinement of these boundaries is
//      deferred; the deterministic split keeps the count sensible without a model call.)
//
// The spans tile each chapter contiguously and gaplessly, and there is never an empty result, so every
// scene can be attributed to a chapter.
public sealed class SceneDetector
{
    // Split a scene once it grows past this, and aim each split piece near TargetSceneChars.
    public const int TargetSceneChars = 5_000;
    public const int MaxSceneChars = 9_000;

    private const int MinSeparatorMarks = 3;

    // A scene/section divider line: a run of ornament characters, optionally spaced ("* * *", "---", "###").
    private static readonly Regex SeparatorRule = new(
        @"^[*#~\-–—•·§_=+]+(?:[ \t]+[*#~\-–—•·§_=+]+)*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Splits the manuscript into ordered scenes. When no chapters are supplied the whole text is treated
    /// as one chapter (scenes cited by number alone). Never returns an empty list.
    /// </summary>
    public IReadOnlyList<SceneSpan> Detect(string fullText, IReadOnlyList<ChapterSpan> chapters)
    {
        fullText ??= string.Empty;
        var effectiveChapters = chapters is { Count: > 0 }
            ? chapters.OrderBy(c => c.Number).ToList()
            : [new ChapterSpan(1, ChapterDetector.WholeManuscriptLabel, 0, fullText.Length)];

        var scenes = new List<SceneSpan>();
        var number = 1;
        foreach (var chapter in effectiveChapters)
        {
            var start = Math.Clamp(chapter.StartOffset, 0, fullText.Length);
            var end = Math.Clamp(chapter.EndOffset, start, fullText.Length);
            foreach (var (spanStart, spanEnd) in SplitChapter(fullText, start, end))
                scenes.Add(new SceneSpan(number++, chapter.Number, chapter.Label, spanStart, spanEnd));
        }

        // A wholly-empty manuscript still gets one scene so the pipeline has a unit to work with.
        if (scenes.Count == 0)
        {
            var first = effectiveChapters[0];
            scenes.Add(new SceneSpan(1, first.Number, first.Label, 0, fullText.Length));
        }
        return scenes;
    }

    // Yields contiguous, gapless [start, end) scene spans for one chapter: authorial breaks first, then a
    // length cap so an unbroken run doesn't become one giant scene.
    private static IEnumerable<(int Start, int End)> SplitChapter(string text, int start, int end)
    {
        var starts = SceneStartOffsets(text, start, end);
        if (starts.Count == 0)
        {
            // Whitespace-only chapter: one span so nothing is left uncovered.
            yield return (start, end);
            yield break;
        }

        for (var i = 0; i < starts.Count; i++)
        {
            // The first scene reaches back to the chapter start so the tiling is gapless (a leading
            // heading line rides with the first scene).
            var spanStart = i == 0 ? start : starts[i];
            var spanEnd = i + 1 < starts.Count ? starts[i + 1] : end;
            foreach (var piece in CapLength(text, spanStart, spanEnd))
                yield return piece;
        }
    }

    // The absolute offsets at which a new scene begins within [start, end): the first content line, and
    // any content line that follows a divider or a two-plus-blank-line gap.
    private static List<int> SceneStartOffsets(string text, int start, int end)
    {
        var starts = new List<int>();
        var pendingBreak = true; // the chapter's first content line opens the first scene
        var blankRun = 0;

        var p = start;
        while (p < end)
        {
            var newline = text.IndexOf('\n', p);
            var lineEnd = newline < 0 || newline >= end ? end : newline + 1;
            var trimmed = text[p..lineEnd].Trim();

            if (trimmed.Length == 0)
            {
                blankRun++;
            }
            else if (IsDivider(trimmed))
            {
                pendingBreak = true;
                blankRun = 0;
            }
            else
            {
                if (blankRun >= 2)
                    pendingBreak = true;
                blankRun = 0;
                if (pendingBreak)
                {
                    starts.Add(p);
                    pendingBreak = false;
                }
            }

            p = lineEnd;
        }
        return starts;
    }

    // Splits an over-long span at paragraph boundaries (blank lines) so each piece sits near
    // TargetSceneChars, hard-cutting only when a single paragraph exceeds MaxSceneChars.
    private static IEnumerable<(int Start, int End)> CapLength(string text, int start, int end)
    {
        if (end - start <= MaxSceneChars)
        {
            yield return (start, end);
            yield break;
        }

        var cursor = start;
        while (end - cursor > MaxSceneChars)
        {
            var target = cursor + TargetSceneChars;
            var boundary = ParagraphBoundaryAtOrAfter(text, target, cursor + MaxSceneChars);
            yield return (cursor, boundary);
            cursor = boundary;
        }
        yield return (cursor, end);
    }

    // The start of the paragraph at or after `from` (a blank-line boundary), bounded by `hardLimit`.
    private static int ParagraphBoundaryAtOrAfter(string text, int from, int hardLimit)
    {
        var i = from;
        while (i < hardLimit)
        {
            var newline = text.IndexOf('\n', i);
            if (newline < 0 || newline >= hardLimit)
                break;
            // A blank line (two consecutive newlines, allowing whitespace between) is a paragraph break.
            var j = newline + 1;
            while (j < text.Length && (text[j] == ' ' || text[j] == '\t' || text[j] == '\r'))
                j++;
            if (j < text.Length && text[j] == '\n')
                return j + 1;
            i = newline + 1;
        }
        return hardLimit;
    }

    private static bool IsDivider(string trimmedLine) =>
        SeparatorRule.IsMatch(trimmedLine) &&
        trimmedLine.Count(c => !char.IsWhiteSpace(c)) >= MinSeparatorMarks;
}
