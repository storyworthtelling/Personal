using System.Text.RegularExpressions;

namespace Constitution.Domain.Voice;

/// <summary>
/// What a seat is handed: which seat, and the facts already reduced to words (an opaque, ordered list —
/// building them is each seat's own facts builder's job). With no voice the seat hides, like every other
/// app's (owner's ruling, W10), so there is no plain sentence to carry.
///
/// <para><b>There is no error anywhere on this type</b> — nothing here takes or carries an exception —
/// because an error has no seat to be voiced in.</para>
/// </summary>
public sealed partial class SeatRequest
{
    public SeatRequest(VoiceSeat seat, IReadOnlyList<string> facts)
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


        Seat = seat;
        Facts = facts.Select(fact => Whitespace().Replace(fact, " ").Trim()).ToArray();
    }

    public VoiceSeat Seat { get; }

    /// <summary>Plain words, in the order the seat's facts builder gave them, each on one line.</summary>
    public IReadOnlyList<string> Facts { get; }


    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>A seat's line in the owner's voice, set in their hand. A seat with no voice has no line at all.</summary>
public sealed record SeatLine(VoiceSeat Seat, string Text);
