namespace Folio.Domain.Pipeline;

/// <summary>
/// Which parts of the revision report are done, as the desk needs them to say what is happening now.
/// Built from the book's stored results; carries no behaviour of its own.
/// </summary>
public sealed record ReportProgress(
    bool StoryReport, bool Craft, bool Voice, bool Findings, bool Expansion, bool SeriesPending,
    int ReadsDone, int ReadsTotal, string? NextRead, bool JacketPending, bool Rendered);

/// <summary>
/// "Writing the revision report" was one sentence for everything from the first reduction to the last
/// page — a day or more of work, with nothing to say which part of it a book had reached. The steps run
/// in a fixed order (the reductions, a sequel's continuity read, the reads, the jacket, the pages), and
/// the current one is the first not yet done.
/// </summary>
public static class ReportStep
{
    public static string Describe(ReportProgress progress)
    {
        if (!progress.StoryReport) return "Drawing the chapter notes into the story report";
        if (!progress.Craft) return "Gathering the line edits, prose and dialogue notes";
        if (!progress.Voice) return "Checking the voice for drift";
        if (!progress.Findings) return "Matching findings against the last draft";
        if (!progress.Expansion) return "Looking for scenes to add";
        if (progress.SeriesPending) return "Checking continuity against the previous book";

        if (progress.ReadsDone < progress.ReadsTotal)
        {
            return $"Reading the whole book through — read {progress.ReadsDone + 1} of {progress.ReadsTotal}" +
                   (string.IsNullOrWhiteSpace(progress.NextRead) ? string.Empty : $": {progress.NextRead}");
        }

        return progress.JacketPending ? "Painting the jacket" : "Composing the report";
    }
}
