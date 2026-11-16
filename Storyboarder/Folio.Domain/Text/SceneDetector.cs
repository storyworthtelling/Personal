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
// Scene breaks come from the author's text and nothing else (owner's ruling, R1–R2):
//   - a chapter start;
//   - a divider line ("* * *", "#", "⁂", "~ ~ ~", "---", a horizontal rule the extractors write as one);
//   - a gap of two or more blank lines (a single blank line is paragraph spacing).
// A chapter with no breaks is ONE scene, however long. There used to be a length cap that cut an unbroken
// chapter at paragraph boundaries every ~5,000 characters; it invented scenes the author never wrote, and
// the scene verdicts paired against those invented scenes were the "muddled separation" the owner saw.
//
// The spans tile each chapter contiguously and gaplessly, and there is never an empty result, so every
// scene can be attributed to a chapter.
public sealed class SceneDetector
{
    // Rule-like marks: a run of these is a divider only when there are at least three (a dash or two in a
    // line of its own is punctuation, not a rule).
    private const string RuleMarks = "-–—_=";
    private const int MinRuleMarks = 3;

    // A divider line: ornament glyphs, optionally spaced ("* * *", "⁂", "#", "~ ~ ~"), or spaced o's.
    private static readonly Regex DividerLine = new(
        @"^(?:[*#~\-–—•·§_=+⁂❦❧☙✻✱✳✦◆◇○●]+(?:[ \t]+[*#~\-–—•·§_=+⁂❦❧☙✻✱✳✦◆◇○●]+)*|o(?:[ \t]+o){2,})$",
        RegexOptions.CultureInvariant);

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

    // Yields contiguous, gapless [start, end) scene spans for one chapter, one per authorial break.
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
            yield return (spanStart, spanEnd);
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

    private static bool IsDivider(string trimmedLine)
    {
        if (!DividerLine.IsMatch(trimmedLine))
            return false;
        var marks = trimmedLine.Where(c => !char.IsWhiteSpace(c)).ToList();
        return marks.All(c => RuleMarks.Contains(c)) ? marks.Count >= MinRuleMarks : marks.Count >= 1;
    }
}
