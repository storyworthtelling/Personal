using System.Text.RegularExpressions;

namespace Constitution.Domain.Voice;

/// <summary>
/// What a seat is handed: which seat, the facts already reduced to words (an opaque, ordered list —
/// building them is each seat's own facts builder's job), and the plain Serif sentence that shows
/// when there is no voice.
///
/// <para><b>The fallback is required and cannot be blank</b>, because the line is never hidden: a
/// seat with nothing to show when the voice is missing is a gap on the page. <b>There is no error
/// anywhere on this type</b> — nothing here takes or carries an exception — because an error has no
/// seat to be voiced in.</para>
/// </summary>
public sealed partial class SeatRequest
{
    public SeatRequest(VoiceSeat seat, IReadOnlyList<string> facts, string fallback)
    {
        if (!Enum.IsDefined(seat))
        {
            throw new ArgumentOutOfRangeException(nameof(seat), seat,
                "This seat is not on Constitution's closed list of six voiced seats.");
        }

        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Count == 0 || facts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "A seat is written from at least one fact, and no fact may be blank.", nameof(facts));
        }

        if (string.IsNullOrWhiteSpace(fallback))
        {
            throw new ArgumentException(
                "A seat needs its plain sentence: the line is never hidden when there is no voice.", nameof(fallback));
        }

        Seat = seat;
        Facts = facts.Select(fact => Whitespace().Replace(fact, " ").Trim()).ToArray();
        Fallback = fallback.Trim();
    }

    public VoiceSeat Seat { get; }

    /// <summary>Plain words, in the order the seat's facts builder gave them, each on one line.</summary>
    public IReadOnlyList<string> Facts { get; }

    /// <summary>The plain Serif sentence built from these same facts, shown when the voice is unavailable.</summary>
    public string Fallback { get; }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>
/// The sentence a seat shows. <see cref="IsVoiced"/> says which face to set it in: the owner's
/// calligraphy hand when voiced, plain Serif when it is the fallback. <see cref="Text"/> is never
/// blank.
/// </summary>
public sealed record SeatLine(VoiceSeat Seat, string Text, bool IsVoiced);
