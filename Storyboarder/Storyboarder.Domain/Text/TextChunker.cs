namespace Storyboarder.Domain.Text;

// One deterministic window over the source novel. Offsets are into the original text so chunking
// is reproducible across runs (re-chunking the same text yields identical spans => resumable).
public sealed record ChunkSpan(int Ordinal, int StartOffset, int EndOffset, string Text);

// Splits a novel into ordered chunks no larger than maxChars, preferring paragraph boundaries and
// packing greedily toward targetChars. Oversized paragraphs are split on sentence/whitespace
// boundaries, falling back to a hard cut only when a single run exceeds maxChars.
public sealed class TextChunker
{
    private readonly int targetChars;
    private readonly int maxChars;

    public TextChunker(int targetChars = 48_000, int maxChars = 60_000)
    {
        if (targetChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetChars), "Target size must be positive.");
        if (maxChars < targetChars)
            throw new ArgumentOutOfRangeException(nameof(maxChars), "Max size must be >= target size.");
        this.targetChars = targetChars;
        this.maxChars = maxChars;
    }

    public IReadOnlyList<ChunkSpan> Chunk(string text)
    {
        var chunks = new List<ChunkSpan>();
        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        int chunkStart = -1;
        int chunkEnd = -1;

        void Emit(int start, int end)
        {
            chunks.Add(new ChunkSpan(chunks.Count, start, end, text.Substring(start, end - start)));
        }

        void Flush()
        {
            if (chunkStart < 0)
                return;
            Emit(chunkStart, chunkEnd);
            chunkStart = chunkEnd = -1;
        }

        foreach (var (start, end) in SplitParagraphs(text))
        {
            if (end - start > maxChars)
            {
                Flush();
                foreach (var (subStart, subEnd) in SplitOversized(text, start, end))
                    Emit(subStart, subEnd);
                continue;
            }

            if (chunkStart < 0)
            {
                chunkStart = start;
                chunkEnd = end;
            }
            else if (end - chunkStart <= targetChars)
            {
                chunkEnd = end;
            }
            else
            {
                Flush();
                chunkStart = start;
                chunkEnd = end;
            }
        }

        Flush();
        return chunks;
    }

    // Yields trimmed paragraph spans, splitting on blank lines (a newline followed, after optional
    // spaces/tabs/CR, by another newline).
    private static IEnumerable<(int Start, int End)> SplitParagraphs(string text)
    {
        int i = 0;
        int n = text.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= n)
                break;

            int start = i;
            int lastNonSpace = i;
            while (i < n)
            {
                if (text[i] == '\n' && IsBlankLineAhead(text, i + 1, n))
                    break;
                if (!char.IsWhiteSpace(text[i]))
                    lastNonSpace = i;
                i++;
            }

            yield return (start, lastNonSpace + 1);
        }
    }

    private static bool IsBlankLineAhead(string text, int from, int n)
    {
        int j = from;
        while (j < n && (text[j] == ' ' || text[j] == '\t' || text[j] == '\r'))
            j++;
        return j < n && text[j] == '\n';
    }

    // Splits a paragraph larger than maxChars into pieces, cutting back to the latest sentence or
    // whitespace boundary within each window when possible.
    private IEnumerable<(int Start, int End)> SplitOversized(string text, int start, int end)
    {
        int pos = start;
        while (pos < end)
        {
            int hardLimit = Math.Min(end, pos + maxChars);
            int cut = hardLimit;
            if (hardLimit < end)
            {
                int preferredFloor = Math.Min(end, pos + targetChars);
                cut = FindBoundaryBackward(text, hardLimit, preferredFloor, pos + 1);
                if (cut <= pos)
                    cut = hardLimit;
            }

            yield return (pos, cut);

            pos = cut;
            while (pos < end && char.IsWhiteSpace(text[pos]))
                pos++;
        }
    }

    // Searches downward from hardLimit for a good cut point: first a sentence end, otherwise any
    // whitespace, preferring positions at or after the preferred floor. Returns the cut index
    // (exclusive end of the piece) or a value <= lowerBound if none was found.
    private static int FindBoundaryBackward(string text, int hardLimit, int preferredFloor, int lowerBound)
    {
        for (int i = hardLimit - 1; i >= lowerBound; i--)
        {
            char c = text[i];
            if ((c == '.' || c == '!' || c == '?' || c == '\n') && i + 1 >= preferredFloor)
                return i + 1;
        }
        for (int i = hardLimit - 1; i >= lowerBound; i--)
        {
            if (char.IsWhiteSpace(text[i]) && i + 1 >= preferredFloor)
                return i + 1;
        }
        return lowerBound - 1;
    }
}
