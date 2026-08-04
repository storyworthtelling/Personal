namespace Storyboarder.Domain.Pipeline;

// One page as the decomposer sees it: its stable id, the chapter it belongs to (0 when the book had no
// detectable chapters), that chapter's heading, and whether the page opens a new scene (its first panel
// is a scene start). The caller supplies pages already in reading order.
public sealed record DecomposablePage(Guid PageId, int ChapterNumber, string ChapterLabel, bool StartsNewScene);

// One planned volume: its 1-based number, cover title, and the pages it holds in reading order (each
// page's new within-volume page number is its position here, 1-based).
public sealed record PlannedVolume(int Number, string Title, IReadOnlyList<Guid> PageIds);

// Groups storyboarded pages into volumes for binding. The rule is one volume per chapter: bounded,
// meaningful units that each bind well within the phone's memory and the background worker's time budget —
// unlike a model-chosen split, which once put 338 pages into a single volume that could never finish.
//
// A chapter with more pages than a single binding can carry is split into the fewest parts that fit, each
// cut snapped to a scene boundary (the storyboard's own NewScene tags) so a volume never breaks mid-scene;
// when no scene start sits within reach of a cut, it falls back to an even page split so the cap always
// holds. Pure and deterministic — no database, no services.
public static class VolumeDecomposer
{
    public static IReadOnlyList<PlannedVolume> Decompose(
        IReadOnlyList<DecomposablePage> pagesInReadingOrder, int maxPagesPerVolume)
    {
        if (maxPagesPerVolume < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPagesPerVolume), maxPagesPerVolume,
                "A volume must be allowed at least one page.");

        var volumes = new List<PlannedVolume>();
        int number = 1;

        foreach (var chapter in GroupIntoChapterRuns(pagesInReadingOrder))
        {
            var parts = SplitToFitCap(chapter, maxPagesPerVolume);
            var label = chapter.Find(p => !string.IsNullOrWhiteSpace(p.ChapterLabel))?.ChapterLabel;
            var chapterNumber = chapter[0].ChapterNumber;

            for (int part = 0; part < parts.Count; part++)
                volumes.Add(new PlannedVolume(
                    number++,
                    Title(label, chapterNumber, part, parts.Count),
                    [.. parts[part].Select(p => p.PageId)]));
        }

        return volumes;
    }

    // Contiguous runs of equal chapter number, preserving reading order. Reading order already keeps a
    // chapter's pages together, so a run is exactly one chapter.
    private static IEnumerable<List<DecomposablePage>> GroupIntoChapterRuns(IReadOnlyList<DecomposablePage> pages)
    {
        var run = new List<DecomposablePage>();
        foreach (var page in pages)
        {
            if (run.Count > 0 && page.ChapterNumber != run[0].ChapterNumber)
            {
                yield return run;
                run = [];
            }
            run.Add(page);
        }
        if (run.Count > 0)
            yield return run;
    }

    // Greedily fills each part as full as the cap allows, cutting at the latest scene start that keeps the
    // part within the cap. With no scene start in reach the cut lands on the cap boundary, so no part ever
    // exceeds maxPagesPerVolume.
    private static List<List<DecomposablePage>> SplitToFitCap(List<DecomposablePage> chapter, int cap)
    {
        var parts = new List<List<DecomposablePage>>();
        int start = 0;
        while (start < chapter.Count)
        {
            int end;
            if (chapter.Count - start <= cap)
                end = chapter.Count;
            else
            {
                // Cut at the latest scene start c with start < c <= start + cap (a scene start begins the
                // next part), else at the cap boundary. start + cap < Count here, so the index is valid.
                end = start + cap;
                for (int c = start + cap; c > start; c--)
                {
                    if (chapter[c].StartsNewScene)
                    {
                        end = c;
                        break;
                    }
                }
            }
            parts.Add(chapter.GetRange(start, end - start));
            start = end;
        }
        return parts;
    }

    private static string Title(string? label, int chapterNumber, int partIndex, int partCount)
    {
        var baseName = !string.IsNullOrWhiteSpace(label) ? label
            : chapterNumber > 0 ? $"Chapter {chapterNumber}"
            : null;

        if (partCount == 1)
            return baseName ?? string.Empty;

        return baseName is null ? $"Part {partIndex + 1}" : $"{baseName} · Part {partIndex + 1}";
    }
}
