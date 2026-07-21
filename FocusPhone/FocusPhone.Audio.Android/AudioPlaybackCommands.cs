namespace FocusPhone.Audio;

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
}
