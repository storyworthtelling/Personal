using System.Security.Cryptography;
using System.Text;

namespace Index.Domain.Voice;

/// <summary>The three places Index speaks in the owner's voice (remediation 3.7, owner decision R5): a
/// closed list, so a fourth is a decision rather than a diff.</summary>
public enum VoiceSeat
{
    /// <summary>Above the account list (mockup 2/11): how the debt has moved, and the loans behind you.</summary>
    Accounts,

    /// <summary>The Loans side, above DEBT-FREE (mockup 6): how far down from the peak.</summary>
    Snowball,

    /// <summary>The Accounts side, under the switch (mockup 9): a monthly rate, never a balance (D114).</summary>
    Savings,
}

/// <summary>
/// Everything a seat's model call may be told, and nothing else: a handful of sentences already reduced to
/// words, percentages and counts (and, on Savings alone, one monthly rate). Built from real data by
/// <see cref="VoiceFactBuilder"/>; <see cref="VoiceSeatPrompts"/> turns it into the request.
///
/// <para>No facts means no line: the seat is hidden rather than asked to improvise.</para>
/// </summary>
public sealed record VoiceFacts(VoiceSeat Seat, IReadOnlyList<string> Lines)
{
    public static VoiceFacts None(VoiceSeat seat) => new(seat, []);

    public bool HasFacts => Lines.Count > 0;

    /// <summary>
    /// A short, stable fingerprint of the seat and its facts — what the composer's per-day cache keys on, so
    /// a re-render with the same facts is not another model call and a change in the facts is.
    /// Not <c>string.GetHashCode</c>, which is randomised per process.
    /// </summary>
    public string Hash
    {
        get
        {
            var text = $"{Seat}\n{string.Join('\n', Lines)}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
        }
    }
}
