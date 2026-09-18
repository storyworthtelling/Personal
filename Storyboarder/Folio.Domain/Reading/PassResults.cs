using Folio.Domain.Analysis;

namespace Folio.Domain.Reading;

/// <summary>
/// What one whole-book section pass comes back with: a short statement of what it found overall, and
/// its findings, each anchored to the chapters it concerns.
///
/// <para>A section is allowed to find nothing — "a section with nothing to say prints nothing, and
/// its pass is skipped". That is the one place the sections and the readers differ: a <i>reader</i>
/// with nothing to say is a failed pass (F44/F46), because a persona that speaks only when it has
/// something easy to say is a different instrument.</para>
/// </summary>
public sealed record SectionRead(string PersonaId, string Summary, IReadOnlyList<ReportFinding> Findings)
{
    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Summary) || Findings is { Count: > 0 };

    public static SectionRead Silent(string personaId) => new(personaId, string.Empty, []);
}

/// <summary>
/// What the two synthesis passes come back with. They are the only passes that read the other
/// fifteen rather than the manuscript, which is why they run last.
/// </summary>
public sealed record SynthesisRead(IReadOnlyList<RevisionItem> RevisionPlan, Verdict Verdict)
{
    public static SynthesisRead Empty { get; } = new([], Verdict.Empty);
}
