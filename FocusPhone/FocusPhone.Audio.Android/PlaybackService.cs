using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.Session;

namespace FocusPhone.Audio;

/// <summary>
/// Background podcast playback: a MediaSessionService hosting ExoPlayer, which
/// provides the media notification, lockscreen controls, and audio focus. It also
/// owns the resume book — a lightweight main-thread poll records the current
/// position into <see cref="PlaybackProgressStore"/> so playback resumes exactly
/// even when listening happens entirely in the background. Skip-silence (an
/// ExoPlayer setting not reachable through MediaController) is toggled via an
/// Intent from the activity.
/// </summary>
[Service(
    Name = "com.storyworthtelling.focusphone.audio.PlaybackService",
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(["androidx.media3.session.MediaSessionService"])]
public class PlaybackService : MediaSessionService
{
    private const long RecordIntervalMs = 10_000;

    private MediaSession? mediaSession;
    private IExoPlayer? player;
    private PlaybackProgressStore? progressStore;
    private PlaybackProgressBook book = new();
    private Handler? handler;
    private Java.Lang.Runnable? recorder;

    public override void OnCreate()
    {
        base.OnCreate();
        progressStore = new PlaybackProgressStore(this);
        book = new PlaybackProgressBook(progressStore.Load());

        player = new ExoPlayerBuilder(this).Build()!;
        mediaSession = new MediaSession.Builder(this, player).Build();

        handler = new Handler(Looper.MainLooper!);
        recorder = new Java.Lang.Runnable(RecordTick);
        handler.PostDelayed(recorder, RecordIntervalMs);
    }

    public override MediaSession? OnGetSession(MediaSession.ControllerInfo controllerInfo) => mediaSession;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == AudioPlaybackCommands.ActionSetSkipSilence && player is not null)
        {
            player.SkipSilenceEnabled = intent.GetBooleanExtra(AudioPlaybackCommands.ExtraEnabled, false);
            return StartCommandResult.Sticky;
        }

        return base.OnStartCommand(intent, flags, startId);
    }

    private void RecordTick()
    {
        RecordCurrentPosition();
        if (handler is not null && recorder is not null)
        {
            handler.PostDelayed(recorder, RecordIntervalMs);
        }
    }

    private void RecordCurrentPosition()
    {
        var mediaId = player?.CurrentMediaItem?.MediaId;
        if (player is null || string.IsNullOrEmpty(mediaId))
        {
            return;
        }

        var position = TimeSpan.FromMilliseconds(player.CurrentPosition);
        var duration = player.Duration > 0
            ? TimeSpan.FromMilliseconds(player.Duration)
            : (TimeSpan?)null;
        book.Record(mediaId!, position, duration, DateTimeOffset.Now);
        progressStore!.Save(book.Export());
    }

    public override void OnDestroy()
    {
        RecordCurrentPosition();
        if (handler is not null && recorder is not null)
        {
            handler.RemoveCallbacks(recorder);
        }

        mediaSession?.Player?.Release();
        mediaSession?.Release();
        mediaSession = null;
        player = null;
        base.OnDestroy();
    }
}
