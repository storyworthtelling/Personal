using Storyworthtelling.CallSheet;

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
/// When to compose again: the suite's one rule (<see cref="SeatPlan"/>, audit W10.3) — at most once per seat,
/// facts and day, an empty attempt tried again after a short wait — said in the words Pulp's pages draw by.
/// A line is a model call, and the home is the screen Pulp opens on, so composing per paint would bill a call
/// per look.
/// </summary>
public static class VoiceLinePlan
{
    public static VoiceLineAction Decide(
        VoiceFacts facts, bool voiceReady, SeatMemory? remembered, DateTimeOffset now, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return SeatPlan.Decide(facts.Occasion.Line, voiceReady, facts.Hash, facts.HasFacts, remembered, now, today) switch
        {
            SeatAction.Compose => VoiceLineAction.Compose,
            SeatAction.Reuse => VoiceLineAction.Reuse,
            _ => Quiet(facts.Seat),
        };
    }

    /// <summary>What a seat shows without a line: nothing, or its neutral line.</summary>
    public static VoiceLineAction Quiet(VoiceSeat seat) =>
        VoicedOccasions.For(seat).Fallback == VoiceFallback.Hide ? VoiceLineAction.Hide : VoiceLineAction.ShowNeutral;
}
