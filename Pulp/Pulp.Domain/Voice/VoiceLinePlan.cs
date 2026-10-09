namespace Pulp.Domain.Voice;

/// <summary>What a seat does on this paint.</summary>
public enum VoiceLineAction
{
    /// <summary>Ask the composer for a new line.</summary>
    Compose,

    /// <summary>Show the line already composed for these facts today.</summary>
    Reuse,

    /// <summary>Show nothing.</summary>
    Hide,

    /// <summary>Show the seat's neutral Serif italic line (D264).</summary>
    ShowNeutral,
}

/// <summary>
/// The last line a seat was given: for which facts, on which day, and the line itself — null or blank
/// when the attempt came back empty (no profile, a failed call), which still counts as the attempt.
/// </summary>
public sealed record RememberedLine(VoiceSeat Seat, string FactsHash, DateOnly Day, string? Line);

/// <summary>
/// When to compose again: at most once per seat, facts and day (Index's composer, Folio's line plan).
/// A line is a model call, and the home is the screen Pulp opens on, so composing per paint would bill
/// a call per look; the line only has something new to say when its facts or the day move.
/// </summary>
public static class VoiceLinePlan
{
    public static VoiceLineAction Decide(VoiceFacts facts, bool voiceReady, RememberedLine? remembered, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var occasion = facts.Occasion;
        var fallback = occasion.Fallback == VoiceFallback.Hide ? VoiceLineAction.Hide : VoiceLineAction.ShowNeutral;

        if (!voiceReady)
        {
            return fallback;
        }

        if (occasion.Fallback == VoiceFallback.Hide && !facts.HasFacts)
        {
            return VoiceLineAction.Hide;
        }

        if (remembered is not null && remembered.Seat == facts.Seat && remembered.Day == today
            && string.Equals(remembered.FactsHash, facts.Hash, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(remembered.Line) ? fallback : VoiceLineAction.Reuse;
        }

        return VoiceLineAction.Compose;
    }
}
