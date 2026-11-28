using System.Security.Cryptography;
using System.Text;

namespace Watch.Domain.Dash;

/// <summary>Whether an unknown caller's call rings, or shows without ringing.</summary>
public enum RingVerdict
{
    Ring,
    Silence,
}

/// <summary>
/// A caller as the wrist remembers them for a few minutes (P14 B8, W-5.1.9): a keyed digest of the number's last ten
/// digits, made with a secret that is new each process and never leaves memory, so no number is kept, logged or sent, and
/// the digest can't be searched for. A call with no digits to it is <see cref="Withheld"/>.
/// </summary>
public static class CallerKey
{
    public const string Withheld = "withheld";

    // The last ten digits, as Post threads a number: +1 212-555-0101 and 2125550101 are one caller.
    private const int Digits = 10;

    private const int SecretBytes = 32;

    public static string Of(string? number, byte[] secret)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
        {
            return Withheld;
        }
        var kept = digits.Length > Digits ? digits[^Digits..] : digits;
        var hash = HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(kept));
        return "k" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);
}

/// <summary>
/// Unknown numbers ring only if they call twice within minutes (A12), day and night alike, since repeat callers ring
/// overnight too. The platform shows Dash only the calls from numbers not in the watch's contacts; a first call is
/// silenced (it still shows, and can be answered), and the same caller again within <see cref="Window"/> rings. Held in
/// memory only.
/// </summary>
public sealed class RepeatCallers
{
    /// <summary>Android's own repeat-caller window, which the watch's Do Not Disturb uses overnight, so the two agree.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>How many callers the wrist remembers at once.</summary>
    public const int Remembered = 32;

    private readonly object gate = new();
    private readonly LinkedList<(string Caller, DateTimeOffset At)> heard = new();

    public static RepeatCallers Shared { get; } = new();

    public RingVerdict Heard(string caller, DateTimeOffset at)
    {
        lock (gate)
        {
            var again = heard.Any(entry => entry.Caller == caller && entry.At <= at && at - entry.At <= Window);
            heard.AddLast((caller, at));
            while (heard.Count > Remembered)
            {
                heard.RemoveFirst();
            }
            return again ? RingVerdict.Ring : RingVerdict.Silence;
        }
    }
}
