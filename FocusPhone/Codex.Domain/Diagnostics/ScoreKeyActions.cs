namespace Codex.Domain.Diagnostics;

/// <summary>
/// Score's key actions (LOG-LOOP-PLAN.md §4.1, O10): the taps that start background work, declared to the
/// tap watch so they are never judged dead and their failure or silence is logged. Wire names in the logs,
/// listed by <c>KeyActionsDeclaredTests</c>, so a new one is a deliberate edit.
/// </summary>
public static class ScoreKeyActions
{
    /// <summary>TIDAL: Sign in, answered by the token exchange.</summary>
    public const string TidalSignIn = "score.tidal-sign-in";

    /// <summary>Music: Rescan the library.</summary>
    public const string Rescan = "score.rescan";

    /// <summary>Playing a track or an episode, answered by the controller.</summary>
    public const string Play = "score.play";

    /// <summary>Podcasts: Mark played, and the bulk forms.</summary>
    public const string MarkPlayed = "score.mark-played";

    /// <summary>Discover: making a playlist from a prompt.</summary>
    public const string DiscoveryPlaylist = "score.discovery-playlist";

    /// <summary>Diagnostics: Send logs now.</summary>
    public const string SendLogsNow = "score.send-logs-now";

    public static IReadOnlyList<string> All { get; } =
        [TidalSignIn, Rescan, Play, MarkPlayed, DiscoveryPlaylist, SendLogsNow];
}
