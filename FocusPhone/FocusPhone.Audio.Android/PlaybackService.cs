using Android.App;
using Android.Content.PM;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.Session;

namespace FocusPhone.Audio;

/// <summary>
/// Background podcast playback: a MediaSessionService hosting ExoPlayer, which
/// provides the media notification, lockscreen controls, and audio focus.
/// </summary>
[Service(
    Name = "com.storyworthtelling.focusphone.audio.PlaybackService",
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(["androidx.media3.session.MediaSessionService"])]
public class PlaybackService : MediaSessionService
{
    private MediaSession? mediaSession;

    public override void OnCreate()
    {
        base.OnCreate();
        var player = new ExoPlayerBuilder(this).Build()!;
        mediaSession = new MediaSession.Builder(this, player).Build();
    }

    public override MediaSession? OnGetSession(MediaSession.ControllerInfo controllerInfo) => mediaSession;

    public override void OnDestroy()
    {
        mediaSession?.Player?.Release();
        mediaSession?.Release();
        mediaSession = null;
        base.OnDestroy();
    }
}
