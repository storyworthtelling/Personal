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
}
