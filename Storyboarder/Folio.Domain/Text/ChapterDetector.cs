using System.Text.RegularExpressions;

namespace Folio.Domain.Text;

// One detected chapter, spanning [StartOffset, EndOffset) of the source text. Number is 1-based
// READING ORDER (a prologue is 1), not necessarily the number printed in the book.
public sealed record ChapterSpan(int Number, string Label, int StartOffset, int EndOffset);

// Finds chapter boundaries in a manuscript from heading heuristics, so every finding in the revision
// report can cite where in the book it came from. Pure and deterministic: the same text always yields
// the same spans, which is what lets detection run exactly once at ingestion and be trusted from then on.
//
// Detection is TIERED, because manuscripts mark their chapters in very different ways and a heuristic
// applied to a book that doesn't use it shreds the chaptering:
//
//   1. STRONG headings — "Chapter 7", "Chapter Seven", "CHAPTER XII: A Reckoning", "Prologue",
//      "Epilogue". TWO OR MORE of these settle it: nothing else is considered, so a shouted line of
//      all-caps dialogue can't insert a chapter break into a book that names its chapters.
//   2. WEAK headings — a lone roman numeral, a bare number, or a standalone all-caps title. Each of
//      those is also an ordinary line of prose in the wrong book, so they are consulted only when the
//      manuscript has no scheme of its own (fewer than two strong headings), only the most populous
//      category is used, and a candidate must open a block (be preceded by a blank line).
//   3. SEPARATORS — "* * *", "---", "###". Last resort; the resulting chapters are labelled
//      "Section N" because the manuscript never named them.
//
// Anything else yields ONE synthesized chapter covering the whole text. The spans are contiguous and
// gapless by construction and there is never an empty result, so every chunk can be attributed.
public sealed class ChapterDetector
{
    /// <summary>Label used when the manuscript has no detectable headings at all.</summary>
    public const string WholeManuscriptLabel = "Whole manuscript";

    /// <summary>Label for the (unheaded) text that precedes the first detected heading.</summary>
    public const string OpeningLabel = "Opening";

    // A heading is a short line of its own. These caps are what keep a line of prose that happens to
    // begin with "Chapter" — or a shouted line of dialogue — from being mistaken for one.
    private const int MaxHeadingChars = 80;
    private const int MaxHeadingWords = 12;
    private const int MaxTitleChars = 60;
    private const int MinTitleLetters = 3;
    private const int MinSeparatorMarks = 3;

    // "Chapter 7", "Chap. 7", "CHAPTER ONE", "Chapter Twenty-One — The Long Road", "Chapter XII: ...".
    // The trailing token is required so the bare word "Chapter" starting a sentence doesn't qualify.
    private static readonly Regex ChapterHeading = new(
        @"^(?:chapter|chap\.)\s*(?:\d{1,3}|[ivxlcdm]{1,8}|[a-z][a-z\-]{2,19})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The divisions a book names instead of numbering.
    private static readonly Regex NamedDivision = new(
        @"^(?:prologue|epilogue|preface|foreword|afterword|interlude|introduction|prelude|coda)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Upper case only: a lower-case "i" or "l" alone is far more likely to be prose than a numeral.
    private static readonly Regex RomanNumeral = new(@"^[IVXLCDM]{1,8}[.)]?$", RegexOptions.CultureInvariant);

    private static readonly Regex BareNumber = new(@"^\d{1,3}[.)]?$", RegexOptions.CultureInvariant);

    // A scene/section rule: runs of the usual ornament characters, optionally spaced ("* * *").
    private static readonly Regex SeparatorRule = new(
        @"^[*#~\-–—•·§_=+]+(?:[ \t]+[*#~\-–—•·§_=+]+)*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Splits the manuscript into ordered, contiguous chapter spans. Never returns an empty list, and
    /// the spans always tile [0, text.Length) exactly.
    /// </summary>
    public IReadOnlyList<ChapterSpan> Detect(string text)
    {
        text ??= string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return [new ChapterSpan(1, WholeManuscriptLabel, 0, text.Length)];

        var lines = ReadLines(text);
        var strong = lines.Where(IsStrongHeading).Select(line => new Heading(line.Start, line.Text)).ToList();

        // Two strong headings are a chaptering scheme; one is not (a lone "Prologue" above a book that
        // numbers its chapters with roman numerals), so a single strong heading is merged with the best
        // weak category rather than standing alone.
        var headings = strong.Count >= 2 ? strong : MergeByOffset(strong, BestWeakCategory(lines));
        if (headings.Count == 0)
            headings = SeparatorHeadings(lines);
        if (headings.Count == 0)
            return [new ChapterSpan(1, WholeManuscriptLabel, 0, text.Length)];

        return BuildSpans(headings, text);
    }

    /// <summary>
    /// The chapter attribution for a span of the source text — typically one chunk's
    /// [startOffset, endOffset). A span lying inside one chapter cites that chapter's label; a span
    /// straddling a break cites the whole range it covers ("Chapters 7–8"), because a finding drawn
    /// from it could have come from either. Returns (0, "") when nothing overlaps.
    /// </summary>
    public static (int Number, string Label) Locate(
        IReadOnlyList<ChapterSpan> chapters, int startOffset, int endOffset)
    {
        // A zero-width span (an empty chunk) still sits somewhere, so probe at least one character.
        var end = Math.Max(endOffset, startOffset + 1);
        var overlapping = chapters
            .Where(chapter => chapter.StartOffset < end && chapter.EndOffset > startOffset)
            .OrderBy(chapter => chapter.Number)
            .ToList();

        if (overlapping.Count == 0)
            return (0, string.Empty);
        if (overlapping.Count == 1)
            return (overlapping[0].Number, overlapping[0].Label);

        return (overlapping[0].Number, $"Chapters {overlapping[0].Number}–{overlapping[^1].Number}");
    }

    // Turns detected heading positions into contiguous spans. Text preceding the first heading becomes
    // its own "Opening" chapter so nothing is left uncovered; when there is none, the first chapter
    // simply starts at 0. A heading with no label of its own is numbered ("Section 3").
    private static IReadOnlyList<ChapterSpan> BuildSpans(IReadOnlyList<Heading> headings, string text)
    {
        var spans = new List<ChapterSpan>();
        var number = 1;

        if (HasContent(text, 0, headings[0].Start))
            spans.Add(new ChapterSpan(number++, OpeningLabel, 0, headings[0].Start));

        for (var i = 0; i < headings.Count; i++)
        {
            var start = spans.Count == 0 ? 0 : headings[i].Start;
            var end = i + 1 < headings.Count ? headings[i + 1].Start : text.Length;
            // Two headings on consecutive lines (a part title above a chapter title) would produce an
            // empty span; fold it into the next one rather than emitting a chapter with no text.
            if (end <= start)
                continue;
            var label = headings[i].Label ?? $"Section {number}";
            spans.Add(new ChapterSpan(number++, label, start, end));
        }

        return spans;
    }

    private static bool HasContent(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return true;
        }
        return false;
    }

    private static bool IsStrongHeading(Line line) =>
        IsHeadingShaped(line.Text) && (ChapterHeading.IsMatch(line.Text) || NamedDivision.IsMatch(line.Text));

    // The most populous weak category, or none. Only one category is used: mixing them lets a stray
    // numeral inside an all-caps-titled book cut a chapter in half. Two candidates are the minimum —
    // a single all-caps line in a whole novel is a shout, not a chaptering scheme.
    private static IReadOnlyList<Heading> BestWeakCategory(IReadOnlyList<Line> lines)
    {
        var openers = lines.Where(line => line.OpensBlock).ToList();
        var categories = new[]
        {
            openers.Where(line => RomanNumeral.IsMatch(line.Text)).ToList(),
            openers.Where(line => BareNumber.IsMatch(line.Text)).ToList(),
            openers.Where(line => IsAllCapsTitle(line.Text)).ToList()
        };

        var best = categories.Where(category => category.Count >= 2).MaxBy(category => category.Count);
        if (best is null)
            return Array.Empty<Heading>();

        return best.Select(line => new Heading(line.Start, line.Text)).ToList();
    }

    // Ornament rules used as the last resort. The sections they cut are unnamed, so their labels are
    // synthesized from the reading order later (Label = null here).
    private static IReadOnlyList<Heading> SeparatorHeadings(IReadOnlyList<Line> lines)
    {
        var rules = lines
            .Where(line => line.OpensBlock && SeparatorRule.IsMatch(line.Text) &&
                           line.Text.Count(character => !char.IsWhiteSpace(character)) >= MinSeparatorMarks)
            .Select(line => new Heading(line.Start, null))
            .ToList();

        return rules.Count >= 2 ? rules : (IReadOnlyList<Heading>)Array.Empty<Heading>();
    }

    private static IReadOnlyList<Heading> MergeByOffset(IReadOnlyList<Heading> first, IReadOnlyList<Heading> second)
    {
        if (second.Count == 0)
            return first;

        return first.Concat(second)
            .GroupBy(heading => heading.Start)
            .Select(group => group.First())
            .OrderBy(heading => heading.Start)
            .ToList();
    }

    private static bool IsHeadingShaped(string trimmed) =>
        trimmed.Length is > 0 and <= MaxHeadingChars && CountWords(trimmed) <= MaxHeadingWords;

    // A standalone title line: no lower-case letters, a few real letters, short, and not the shape of
    // shouted dialogue (quoted, or ending in ! / ?).
    private static bool IsAllCapsTitle(string trimmed)
    {
        if (trimmed.Length is 0 or > MaxTitleChars)
            return false;
        if (trimmed.EndsWith('!') || trimmed.EndsWith('?') || trimmed.Contains('"') || trimmed.Contains('“'))
            return false;

        var letters = 0;
        foreach (var character in trimmed)
        {
            if (char.IsLower(character))
                return false;
            if (char.IsLetter(character))
                letters++;
        }
        return letters >= MinTitleLetters;
    }

    private static int CountWords(string trimmed)
    {
        var words = 0;
        var inWord = false;
        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }
        return words;
    }

    // Splits the text into lines, recording each line's start offset in the ORIGINAL text (so a chapter
    // span begins at its heading) and whether it opens a block — the first line, or preceded by a blank
    // line. Handles both \n and \r\n.
    private static IReadOnlyList<Line> ReadLines(string text)
    {
        var lines = new List<Line>();
        var previousBlank = true;
        var index = 0;

        while (index <= text.Length)
        {
            var breakIndex = text.IndexOf('\n', index);
            var end = breakIndex < 0 ? text.Length : breakIndex;
            var content = text[index..end].Trim();
            lines.Add(new Line(index, content, OpensBlock: previousBlank));
            previousBlank = content.Length == 0;

            if (breakIndex < 0)
                break;
            index = breakIndex + 1;
        }

        return lines;
    }

    // One line of the manuscript: where it starts in the original text, its trimmed content, and
    // whether a blank line (or the start of the text) precedes it.
    private readonly record struct Line(int Start, string Text, bool OpensBlock);

    // A detected boundary. A null label means "name it from the reading order" (the separator tier).
    private readonly record struct Heading(int Start, string? Label);
}
