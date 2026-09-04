namespace Codex.Domain.Music;

/// <summary>
/// The durable, propagatable summary of the owner's music taste — the Taste Profile equivalent of the
/// Voice library's VoiceProfile. Refined by the Claude engine from <see cref="TasteEvidence"/> and stored
/// as JSON in Drive; kept compact so the whole thing can ride in a Discovery prompt. An empty profile means
/// nothing has been learned yet and callers should fall back to neutral behavior.
/// </summary>
/// <param name="Summary">A short prose read of the taste — what the owner reaches for and when.</param>
/// <param name="Loves">Strong positives ("bebop piano", "late-night ambient").</param>
/// <param name="Leans">Softer tendencies ("prefers albums over singles").</param>
/// <param name="Avoids">What reads as not-for-them, to keep Discovery from pushing it.</param>
/// <param name="ListensBy">Time-of-day tendencies phrased for a prompt ("mornings skew upbeat").</param>
/// <param name="ExplicitAdvisory">Claude-inferred: the owner appears to avoid explicit content.</param>
/// <param name="SessionsAnalyzed">How many distinct sittings fed this profile — provenance.</param>
public sealed record TasteProfile(
    string Summary,
    IReadOnlyList<string> Loves,
    IReadOnlyList<string> Leans,
    IReadOnlyList<string> Avoids,
    IReadOnlyList<string> TopGenres,
    IReadOnlyList<string> TopArtists,
    IReadOnlyList<string> ListensBy,
    bool ExplicitAdvisory,
    int SessionsAnalyzed)
{
    public static TasteProfile Empty { get; } = new(string.Empty, [], [], [], [], [], [], false, 0);

    public bool IsEmpty => SessionsAnalyzed <= 0 || string.IsNullOrWhiteSpace(Summary);
}
