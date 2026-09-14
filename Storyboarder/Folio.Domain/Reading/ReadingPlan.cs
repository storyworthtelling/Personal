namespace Folio.Domain.Reading;

/// <summary>One pass over the whole manuscript. Seventeen of these make a read.</summary>
public sealed record ReadingPass(string Id, string Name, ModelTier Tier, Persona? Persona)
{
    /// <summary>True for the two passes that read the other fifteen rather than the manuscript.</summary>
    public bool IsSynthesis => Persona is null;
}

/// <summary>
/// The order the seventeen passes run in, and how long they take.
///
/// <para><b>The durations are measured, not guessed</b> (F49). On a real 90,000-word novel a
/// Sonnet section pass took 37–44 seconds and an Opus reader pass 64–66, and those figures are what
/// told the phase the passes could not fit the worker's nine-minute cycle — 905 seconds against 540.
/// They are kept here rather than in a comment because the estimate the owner is shown while the
/// phone reads their book is built from them, and an estimate built from a guess is worse than none.
/// They are a <b>floor</b>: both reader passes in that measurement stopped at the output cap rather
/// than finishing.</para>
///
/// <para>The order matters for one reason: every pass after the first reads the manuscript from
/// cache, and a gap of more than five minutes lets that cache expire. Running them back to back in
/// one foreground session is what keeps the input free.</para>
/// </summary>
public static class ReadingPlan
{
    /// <summary>Measured: a whole-manuscript section pass on the standard model.</summary>
    public static readonly TimeSpan SectionPass = TimeSpan.FromSeconds(40);

    /// <summary>Measured: a whole-manuscript reader pass on the strong model, capped at 4,000 tokens.</summary>
    public static readonly TimeSpan ReaderPass = TimeSpan.FromSeconds(65);

    /// <summary>The two passes that read the fifteen reads: the to-do list and the verdict.</summary>
    public static IReadOnlyList<ReadingPass> Synthesis { get; } =
    [
        new("todo", "Drawing the to-do list together", ModelTier.Strong, null),
        new("verdict", "Writing the verdict", ModelTier.Strong, null),
    ];

    /// <summary>
    /// Sections first, then readers, then synthesis. Sections come first because they are the cheap
    /// model: if a read is going to fail on a key or a quota it fails early and cheaply, rather than
    /// after seven passes of the expensive one.
    /// </summary>
    public static IReadOnlyList<ReadingPass> Passes { get; } =
    [
        .. PersonaCatalogue.Sections.Select(persona =>
            new ReadingPass(persona.Id, persona.Name, persona.Tier, persona)),
        .. PersonaCatalogue.Readers.Select(persona =>
            new ReadingPass(persona.Id, persona.Name, persona.Tier, persona)),
        .. Synthesis,
    ];

    /// <summary>How long the whole read takes, from the measured per-pass figures.</summary>
    public static TimeSpan Estimate => TimeSpan.FromSeconds(
        Passes.Sum(pass => (pass.Tier == ModelTier.Standard ? SectionPass : ReaderPass).TotalSeconds));
}
