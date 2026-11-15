namespace Folio.Domain.Reading;

/// <summary>
/// The genre the consolidation read out of the text, and the word-count range its market expects —
/// told to every whole-book pass, because genre is what decides whether a draft is long or short.
///
/// <para>It comes from the chapter notes' consolidation, not from another pass's opinions: a label
/// and a range, never a finding, so telling it to a reader does not carry anyone else's notes.</para>
/// </summary>
public sealed record GenreFrame(string Genre, string WordCountRange)
{
    /// <summary>No genre was read (an older analysis, or a reply that named none).</summary>
    public static GenreFrame Unknown { get; } = new(string.Empty, string.Empty);

    public bool IsKnown => !string.IsNullOrWhiteSpace(Genre);
}
