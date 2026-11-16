using System.Security.Cryptography;
using System.Text;

namespace Storyworthtelling.CallSheet;

/// <summary>What a seat does on this paint.</summary>
public enum SeatAction
{
    /// <summary>Ask for a new line.</summary>
    Compose,

    /// <summary>Show the line already written for these facts today.</summary>
    Reuse,

    /// <summary>Show the seat's fallback: nothing, or its plain line.</summary>
    Fallback,
}

/// <summary>What a seat was last given: for which facts, on which day, when, and the line (null when it came back
/// empty, which still counts as the attempt).</summary>
public sealed record SeatMemory(string FactsHash, DateOnly Day, string? Line, DateTimeOffset At);

/// <summary>
/// The one rule for when a seat is composed (audit W10.3), for every app: at most once per seat, facts and day;
/// kept across a restart (<see cref="SeatMemoryFile"/>); and an attempt that came back empty (no profile yet, a
/// key not yet restored, a call that did not land) is tried again after <see cref="RetryAfter"/>, not the next
/// day.
/// </summary>
public static class SeatPlan
{
    /// <summary>How long an empty attempt is believed before it is asked again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    public static SeatAction Decide(
        VoiceLine line, bool voiceReady, string factsHash, bool hasFacts, SeatMemory? remembered,
        DateTimeOffset now, DateOnly today)
    {
        if (!voiceReady)
        {
            return SeatAction.Fallback;
        }

        // A hidden seat with nothing to say is not asked to improvise; an empty page is still worth a line.
        if (!hasFacts && line.Fallback == VoiceFallback.Hide)
        {
            return SeatAction.Fallback;
        }

        if (remembered is not null && remembered.Day == today
            && string.Equals(remembered.FactsHash, factsHash, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(remembered.Line))
            {
                return SeatAction.Reuse;
            }
            if (now - remembered.At < RetryAfter)
            {
                return SeatAction.Fallback;
            }
        }

        return SeatAction.Compose;
    }

    /// <summary>The day a line belongs to: an evening that runs past midnight is the evening of the day it began,
    /// so the day turns at five in the morning.</summary>
    public static DateOnly Today(DateTime local) => DateOnly.FromDateTime(local.Hour < 5 ? local.AddDays(-1) : local);

    /// <summary>What to keep after an attempt; a blank line is kept as null.</summary>
    public static SeatMemory Remember(string factsHash, DateOnly today, string? line, DateTimeOffset now) =>
        new(factsHash, today, string.IsNullOrWhiteSpace(line) ? null : line.Trim(), now);

    /// <summary>A short, stable fingerprint of a seat and its facts (never <c>string.GetHashCode</c>, which is
    /// randomised per process).</summary>
    public static string Hash(VoiceLine line, IEnumerable<string> facts)
    {
        var text = $"{line.App}/{line.Seat}\n{string.Join('\n', facts)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }
}
