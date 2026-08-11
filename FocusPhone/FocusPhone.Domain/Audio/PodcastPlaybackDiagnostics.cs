namespace FocusPhone.Domain.Audio;

/// <summary>
/// The owner-facing sentences shown when a podcast play or auto-arrange can't
/// proceed. Kept here — pure and unit-tested — so the wording is verified and the
/// Android head stays a thin caller. These are failure diagnostics: successes stay
/// silent inline state changes (DESIGN.md §5), and the full stack trace of any
/// underlying exception goes to the rolling diagnostic log, not to the toast.
/// </summary>
public static class PodcastPlaybackDiagnostics
{
    /// <summary>The media controller is still connecting (async on cold start).</summary>
    public const string ControllerNotReady = "Audio is still starting — tap the episode again in a moment.";

    /// <summary>The episode has no playable enclosure, so it can't start.</summary>
    public static string NoPlayableAudio(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? "This episode has no playable audio yet."
            : $"“{title}” has no playable audio yet.";

    /// <summary>Auto-arrange found no unplayed episodes to queue.</summary>
    public static string NothingToQueue(bool isRefreshing) =>
        isRefreshing
            ? "Still fetching episodes — try again once they load."
            : "No unplayed episodes to queue yet.";
}
