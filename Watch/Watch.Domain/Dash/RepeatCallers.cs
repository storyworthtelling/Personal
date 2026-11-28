using System.Globalization;
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
/// digits, sealed with a key the wrist can't give out (on the watch, one the platform's keystore holds), so no number is
/// kept, logged or sent, and a kept digest can't be searched for anywhere else. A call with no digits is <see cref="Withheld"/>.
/// </summary>
public static class CallerKey
{
    public const string Withheld = "withheld";

    // The last ten digits, as Post threads a number: +1 212-555-0101 and 2125550101 are one caller.
    private const int Digits = 10;

    private const int HexDigits = 32;

    /// <summary>The caller sealed by <paramref name="seal"/>, a keyed digest the wrist can't undo, of the number's digits.</summary>
    public static string Of(string? number, Func<byte[], byte[]> seal)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
        {
            return Withheld;
        }
        var kept = digits.Length > Digits ? digits[^Digits..] : digits;
        return "k" + Convert.ToHexString(seal(Encoding.ASCII.GetBytes(kept)))[..HexDigits].ToLowerInvariant();
    }

    /// <summary>The same, sealed with a secret the caller holds (an HMAC-SHA256).</summary>
    public static string Of(string? number, byte[] secret) => Of(number, digits => HMACSHA256.HashData(secret, digits));

    /// <summary>Whether a kept line names a caller as this class writes one.</summary>
    internal static bool IsOne(string caller) =>
        caller == Withheld
        || (caller.Length == HexDigits + 1 && caller[0] == 'k' && caller.Skip(1).All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'));
}

/// <summary>
/// Unknown numbers ring only if they call twice within minutes (A12), day and night alike, since repeat callers ring
/// overnight too. The platform shows Dash only the calls from numbers not in the watch's contacts; a first call is
/// silenced (it still shows, and can be answered), and the same caller again within <see cref="Window"/> rings. The process
/// can die between two calls minutes apart, so the head keeps the callers across a restart as <see cref="Record"/> writes
/// them, a sealed key and a moment each, and never past the window.
/// </summary>
public sealed class RepeatCallers
{
    /// <summary>Android's own repeat-caller window, which the watch's Do Not Disturb uses overnight, so the two agree.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>How many callers the wrist remembers at once.</summary>
    public const int Remembered = 32;

    private readonly object gate = new();
    private readonly LinkedList<(string Caller, DateTimeOffset At)> heard = new();

    /// <summary>The callers heard within the window as one line each, a key and a moment, for the head to keep that long.</summary>
    public string Record(DateTimeOffset now)
    {
        lock (gate)
        {
            return string.Join('\n', heard
                .Where(entry => now - entry.At <= Window)
                .Select(entry => $"{entry.Caller}\t{entry.At.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}"));
        }
    }

    /// <summary>The callers a record kept, less those past the window; a record that won't read is nobody.</summary>
    public static RepeatCallers From(string? record, DateTimeOffset now)
    {
        var callers = new RepeatCallers();
        var lines = (record ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<(string Caller, DateTimeOffset At)>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length != 2 || !CallerKey.IsOne(parts[0])
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
                || milliseconds > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            {
                // One line that won't read means the whole record is in doubt: nobody is remembered.
                return callers;
            }
            kept.Add((parts[0], DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)));
        }
        foreach (var entry in kept.Where(entry => now - entry.At <= Window).TakeLast(Remembered))
        {
            callers.heard.AddLast(entry);
        }
        return callers;
    }

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
