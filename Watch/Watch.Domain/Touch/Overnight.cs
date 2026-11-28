namespace Watch.Domain.Touch;

/// <summary>
/// The watch's own Do Not Disturb, as the platform says it (<c>NotificationManager.CurrentInterruptionFilter</c>): every
/// interruption, priority only, alarms only, none, or not known. Bedtime mode on the watch holds one of the quiet three.
/// </summary>
public enum Interruptions
{
    Unknown,
    All,
    Priority,
    None,
    Alarms,
}

/// <summary>How the wrist buzzes: as a notification, or, for a favourite overnight, as someone asking for the owner.</summary>
public enum BuzzUsage
{
    /// <summary>A notification's buzz, which the platform mutes for the silent ringer and the watch's Do Not Disturb.</summary>
    Notification,

    /// <summary>A request to talk, which the watch's Do Not Disturb lets through when it lets calls through.</summary>
    CommunicationRequest,
}

/// <summary>
/// When the night is (P14 B8, A12, WD §7: "Overnight only favourites and repeat callers buzz"). Nothing on the watch keeps
/// hours of its own: the night is whenever the owner's own Do Not Disturb or Bedtime mode on the watch holds, on the
/// schedule the owner set there, and the day is every other time, a filter the platform won't say included.
/// </summary>
public static class Overnight
{
    public static bool During(Interruptions interruptions) => interruptions is Interruptions.Priority or Interruptions.Alarms or Interruptions.None;
}
