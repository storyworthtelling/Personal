namespace Score;

/// <summary>
/// The Intent contract the activity uses to drive ExoPlayer-only features on the
/// background <see cref="PlaybackService"/>. Skip-silence is an ExoPlayer setting
/// not exposed through <c>MediaController</c>, so it is toggled by a service
/// Intent rather than a session command.
/// </summary>
public static class AudioPlaybackCommands
{
    public const string ActionSetSkipSilence = "com.storyworthtelling.focusphone.audio.SET_SKIP_SILENCE";
    public const string ExtraEnabled = "enabled";

    /// <summary>Re-read the saved equalizer/loudness settings and apply them to the player's audio session.</summary>
    public const string ActionApplyAudioEffects = "com.storyworthtelling.focusphone.audio.APPLY_AUDIO_EFFECTS";

    /// <summary>
    /// Tells <see cref="PlaybackService"/> which session — <see cref="PlaybackService.DriveSessionId"/> or
    /// <see cref="PlaybackService.TidalSessionId"/> — is the one actually playing. The service needs this
    /// even though <c>ScorePlayback</c> already knows which <c>MediaController</c> it just started, because
    /// an external surface (lockscreen, Bluetooth, Android Auto) connects straight to the service without
    /// going through <c>ScorePlayback</c> at all, and <c>OnGetSession</c>'s default (no connection hints)
    /// branch has to know which of the two sessions to hand back.
    /// </summary>
    public const string ActionSetActiveSession = "com.storyworthtelling.focusphone.audio.SET_ACTIVE_SESSION";
    public const string ExtraSessionId = "sessionId";
}
