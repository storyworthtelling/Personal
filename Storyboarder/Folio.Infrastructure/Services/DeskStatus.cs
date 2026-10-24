using Folio.Domain.Pipeline;

namespace Folio.Infrastructure.Services;

/// <summary>What the desk shows for one book: a heading, one status sentence, and — if something is wrong — why.</summary>
public sealed record DeskStatusLine(string Heading, string Summary, string? Problem, bool Stuck);

/// <summary>
/// What the desk says about a book, in one place, so the heading, the sentence under the title and the
/// note the owner's voice writes cannot disagree with each other. They did: a heading that said "Reading
/// now" over a book that had stopped, a sentence that said "Writing the revision report" for a day of
/// work, an error that was recorded and never shown, and a voiced note that said "Draft 1 is complete"
/// over all of it.
/// </summary>
public static class DeskStatus
{
    public static DeskStatusLine Of(NovelStatus s)
    {
        var active = Active(s);
        var stuck = active && s.NeedsAttention;
        var core = Core(s);

        var summary = s.Stage switch
        {
            "Completed" => "Review delivered.",
            "Failed" => "Stopped.",
            "Cancelled" => "Stopped by you.",
            _ when stuck => $"Stopped · {core}",
            _ => core + "…",
        };

        var heading = stuck ? "Needs you" : active ? "Reading now" : "Latest";
        return new DeskStatusLine(heading, summary, Problem(s, active, stuck), stuck);
    }

    /// <summary>
    /// What the owner's voice is told about the book, or null when it should say nothing at all (the book
    /// has stopped, and the desk is showing why). The model is told in words whether the book is finished,
    /// because given only a stage's name it guesses — and guessed "complete".
    /// </summary>
    public static string? VoiceFacts(NovelStatus s)
    {
        switch (s.Stage)
        {
            case "Completed":
                return "The revision report is finished and delivered.";
            case "Failed":
                return "The review stopped with an error and did not finish. " +
                       "Do not say or imply that it is complete.";
            case "Cancelled":
                return "The owner stopped this review before it finished. " +
                       "Do not say or imply that it is complete.";
        }

        if (s.NeedsAttention)
            return null;

        return $"The review is still in progress and is NOT finished. Right now: {Core(s)}. " +
               "Do not say or imply that the draft or the report is complete.";
    }

    private static bool Active(NovelStatus s) => s.Stage is not ("Completed" or "Failed" or "Cancelled");

    // The sentence for the stage, without its trailing ellipsis. The report stage names the step.
    private static string Core(NovelStatus s) => s.Stage switch
    {
        "Created" or "Ingesting" => "Reading the manuscript",
        "BuildingDesignBible" => Complete(s.Chunks)
            ? Count("Drawing reference sheets", "sheets", s.ReferenceSheets)
            : Count("Building the design bible", "chunks", s.Chunks),
        "RenderingSceneImages" => Count("Illustrating the scenes", "scenes", s.SceneImages),
        "ComposingAnalysis" => s.Step ?? Count("Analysing the manuscript", "chapters", s.AnalysisNotes),
        "Archiving" => "Delivering the report",
        _ => s.Stage,
    };

    private static string? Problem(NovelStatus s, bool active, bool stuck)
    {
        // A finished review can still have missed the tablet. That is not a stopped book, but it is
        // the one thing the owner most needs to see on a book that says "delivered".
        if (s.Stage == "Completed")
            return string.IsNullOrWhiteSpace(s.DeliveryNote) ? null : s.DeliveryNote;

        var plain = PlainError.Describe(s.LastError);
        if (plain.Length == 0 || s.Stage is "Cancelled")
            return null;

        var said = char.ToUpperInvariant(plain[0]) + plain[1..];

        if (!active)
            return said.EndsWith('.') ? said : said + ".";

        if (stuck)
            return $"{said}. Folio has stopped trying — tap the book, then Retry now.";

        return s.Attempts > 0
            ? $"{said} — {s.Attempts} of {s.MaxAttempts} tries used; trying again."
            : $"{said} — waiting to try again.";
    }

    private static bool Complete(PhaseProgress phase) => phase.Total > 0 && phase.Done >= phase.Total;

    private static string Count(string verb, string noun, PhaseProgress phase) =>
        phase.Total > 0 ? $"{verb} — {noun} {phase.Done} of {phase.Total}" : verb;
}
