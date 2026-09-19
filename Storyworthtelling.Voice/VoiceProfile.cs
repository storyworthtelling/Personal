namespace Storyworthtelling.Voice;

/// <summary>
/// A phrasing drawn from the owner's own writing, and the book it was drawn from.
///
/// <para>The project id is not provenance for its own sake: it is what lets one book's contribution
/// be taken back out (<see cref="VoiceProfile.Discard"/>) without throwing away a voice that four
/// books taught. As a bare string an example could only ever be discarded with all the others.</para>
/// </summary>
public sealed record VoiceExample(string Text, Guid ProjectId);

/// <summary>
/// A distilled description of the owner's writing voice, learned from the documents they upload and
/// used to write situational app messages that sound like them. Kept compact so the whole profile
/// rides in every message-generation prompt, and stored as JSON in the owner's Drive so every
/// com.storyworthtelling app shares one voice.
///
/// <para><b>Three named fields rather than a paragraph.</b> This used to open with a
/// <c>Summary</c> — a paragraph describing tone, register, attitude and cadence together — and that
/// is what made the profile impossible to work with: a paragraph cannot be checked, cannot be
/// diffed between drafts, and cannot be partly discarded. <see cref="Register"/> is how the owner
/// sounds, <see cref="Cadence"/> is the shape of their sentences, and <see cref="Punctuation"/> is
/// their marks. The old <c>SignatureMoves</c> held the last two together, which is why "em-dash
/// asides" and "short declarative openers" were filed as one thing when they are different
/// instructions to write under.</para>
/// </summary>
public sealed record VoiceProfile(
    /// <summary>How the owner sounds, in a few words: "wry, warm, economical".</summary>
    string Register,
    /// <summary>Short descriptors — "plain-spoken", "unhurried", "dry".</summary>
    IReadOnlyList<string> Traits,
    /// <summary>
    /// Phrasings from the owner's own writing, anchoring the voice concretely. These are STICKY:
    /// each refine adds a few from the new document and never drops what is already learned, so the
    /// anchors accumulate as more books feed the profile.
    /// </summary>
    IReadOnlyList<VoiceExample> Examples,
    /// <summary>Things that would read as not-them: "corporate boilerplate", "hype".</summary>
    IReadOnlyList<string> Avoid,
    /// <summary>Sentence shapes: "short declarative openers", "lists of three".</summary>
    IReadOnlyList<string> Cadence,
    /// <summary>Punctuation habits: "em-dash asides", "semicolons, rarely".</summary>
    IReadOnlyList<string> Punctuation,
    /// <summary>How many documents have fed this profile — provenance, and how settled it is.</summary>
    int DocumentsAnalyzed)
{
    public static VoiceProfile Empty { get; } = new(string.Empty, [], [], [], [], [], 0);

    /// <summary>
    /// Nothing has been learned yet, so there is no voice to write in and the caller falls back to
    /// its neutral copy. Every consumer already honours this.
    /// </summary>
    public bool IsEmpty => DocumentsAnalyzed <= 0 || string.IsNullOrWhiteSpace(Register);

    /// <summary>
    /// A voice was learned, but in a shape this version cannot read, so it has to be learned again.
    ///
    /// <para>Deliberately <b>not</b> the same question as <see cref="IsEmpty"/>. Both mean "print
    /// neutral copy now", but only this one means something was lost — and a first run has nothing
    /// to relearn. Collapsing the two is how a refit silently un-voices every app in the suite.</para>
    /// </summary>
    public bool NeedsRelearn { get; init; }

    /// <summary>
    /// Takes one book's contribution back out and leaves the rest of the voice standing.
    ///
    /// <para>The register, traits and habits are not re-derived — they were learned from every book
    /// together and this cannot un-learn them — so what a discard actually removes is that book's
    /// anchors and its place in the count. When it was the only book, the profile empties: a
    /// register with no writing behind it is a description of nobody.</para>
    /// </summary>
    public VoiceProfile Discard(Guid projectId)
    {
        var kept = Examples.Where(example => example.ProjectId != projectId).ToList();
        if (kept.Count == Examples.Count)
        {
            return this;
        }

        var remaining = Math.Max(DocumentsAnalyzed - 1, 0);
        return remaining == 0
            ? Empty
            : this with { Examples = kept, DocumentsAnalyzed = remaining };
    }
}
