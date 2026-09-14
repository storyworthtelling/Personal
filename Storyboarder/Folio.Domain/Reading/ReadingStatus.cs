namespace Folio.Domain.Reading;

/// <summary>
/// What the phone says while it is reading a novel.
///
/// <para>DESIGN.md §5: loading is a status sentence, not a spinner. That rule was written for a
/// screen, and it matters more here than anywhere else in the suite — this is a quarter of an hour
/// of work behind a notification, and "Working…" for fifteen minutes is indistinguishable from a
/// hang. The sentence names the voice currently reading, so the wait is legible as progress.</para>
///
/// <para>It is here, pure, rather than in the head, because the same sentence has two consumers —
/// the foreground notification and the project's own line on the desk — and two copies of a sentence
/// drift the way two copies of a palette do.</para>
/// </summary>
public static class ReadingStatus
{
    /// <summary>
    /// The line for a read that is <paramref name="done"/> passes in, with
    /// <paramref name="current"/> running.
    /// </summary>
    public static string Sentence(ReadingPass? current, int done, int total)
    {
        if (total <= 0)
        {
            return "Reading the manuscript…";
        }
        if (current is null)
        {
            return done >= total ? "Read." : "Reading the manuscript…";
        }

        var position = $"{Math.Clamp(done + 1, 1, total)} of {total}";
        if (current.IsSynthesis)
        {
            return $"{current.Name} — {position}.";
        }

        var voice = current.Persona!.Kind == PersonaKind.Reader
            ? $"Reading as the {Lowered(current.Name)}"
            : $"Reading for {Lowered(current.Name)}";
        return $"{voice} — {position}.";
    }

    /// <summary>
    /// Roughly how much of the read is left, as a sentence. Built from the measured per-pass figures
    /// (F49), which are a floor — so it says "about", and never counts down to a promise.
    /// </summary>
    public static string Remaining(int done, int total)
    {
        var left = ReadingPlan.Passes.Skip(Math.Max(done, 0)).Take(Math.Max(total - done, 0))
            .Sum(pass => (pass.Tier == ModelTier.Standard
                ? ReadingPlan.SectionPass : ReadingPlan.ReaderPass).TotalMinutes);

        return left < 1 ? "less than a minute left" : $"about {Math.Round(left):0} minutes left";
    }

    // A persona's name is written as a heading ("Story integrity", "Genre fan"); mid-sentence it is
    // not a heading any more. Only the first letter moves — "Graduate literature professor" keeps
    // nothing capitalised, but a name that carried a proper noun would, and none does.
    private static string Lowered(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
