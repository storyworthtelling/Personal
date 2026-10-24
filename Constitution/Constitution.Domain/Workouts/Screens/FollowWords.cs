namespace Constitution.Domain.Workouts.Screens;

/// <summary>What the calendar's answers add up to, for the tab.</summary>
public enum FollowVerdict
{
    /// <summary>Every event is where the change put it, or there was none to move. Nothing to say.</summary>
    Settled,

    /// <summary>An event the owner had moved was left where they put it; the plan changed and the calendar was not made to follow.</summary>
    LeftAlone,

    /// <summary>The calendar was refused or could not be reached and no event moved: it is exactly as it was, and asking again is safe.</summary>
    NotChanged,

    /// <summary>Some events moved and some did not. Asking again would move the wrong ones, so the owner is told instead.</summary>
    PartlyChanged,
}

/// <summary>What the tab says about the calendar after a change, and whether the change owes the calendar another try.</summary>
public sealed record FollowReading(FollowVerdict Verdict, string? Sentence, bool WillRetry);

/// <summary>
/// S2: reading what the calendar answered when it was asked to follow a swap or a skip. The answer used to be dropped, so a refused edit
/// left the event where it was while the tab showed the change, and nothing said so. Here every answer becomes one sentence (or none, when
/// the calendar did what it was asked), in the plain words of a status line: no code, no blame, no promise the app cannot keep. "It will try
/// again" is said only where trying again is safe (<see cref="FollowVerdict.NotChanged"/>).
/// </summary>
public static class FollowWords
{
    /// <summary>The swap could not be written down, so the tab has taken it back.</summary>
    public const string SwapNotKept = "The swap could not be saved, so the plan is as it was.";

    /// <summary>The skip could not be written down yet; the day stays struck through and it is tried again.</summary>
    public const string SkipNotKept = "The skip could not be saved yet. It will try again.";

    public const string CalendarUnchanged = "The calendar did not change. It will try again.";

    public const string CalendarPartlyChanged = "Only part of the calendar changed. Look it over.";

    public const string CalendarLeftAlone = "The calendar was left as you had it.";

    /// <summary>The calendar was asked again as often as it will be, and still did not change.</summary>
    public const string CalendarGaveUp = "The calendar still did not change.";

    public static FollowReading Judge(IReadOnlyList<FollowOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var refused = outcomes.Any(outcome => outcome is FollowOutcome.Failed or FollowOutcome.Unavailable);
        var changed = outcomes.Any(outcome => outcome is FollowOutcome.Moved or FollowOutcome.Removed);
        if (refused)
        {
            return changed
                ? new FollowReading(FollowVerdict.PartlyChanged, CalendarPartlyChanged, false)
                : new FollowReading(FollowVerdict.NotChanged, CalendarUnchanged, true);
        }

        return outcomes.Contains(FollowOutcome.OwnerHadMovedIt)
            ? new FollowReading(FollowVerdict.LeftAlone, CalendarLeftAlone, false)
            : new FollowReading(FollowVerdict.Settled, null, false);
    }
}
