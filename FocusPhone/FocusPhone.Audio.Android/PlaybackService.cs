using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.Session;
using FocusPhone.Audio.Google;
using FocusPhone.Domain.Audio;
using FocusPhone.Domain.Music;

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
[IntentFilter(["androidx.media3.session.MediaLibraryService"])]
public class PlaybackService : MediaLibraryService
{
    private const long RecordIntervalMs = 10_000;

    private MediaLibrarySession? mediaSession;
    private IExoPlayer? player;
    private PlaybackProgressStore? progressStore;
    private PlaybackProgressBook book = new();
    private Handler? handler;
    private Java.Lang.Runnable? recorder;
    private EqualizerStore? equalizerStore;
    private readonly AudioEffects audioEffects = new();

    public override void OnCreate()
    {
        base.OnCreate();
        progressStore = new PlaybackProgressStore(this);
        book = new PlaybackProgressBook(progressStore.Load());

        // Drive-backed music (drive://{fileId}) resolves to an authed download URL per
        // open; podcast http(s) URIs pass through. One factory serves both sides.
        var resolvingFactory = new ResolvingDataSource.Factory(
            new DefaultHttpDataSource.Factory(),
            new DriveMediaResolver(new DeviceAccountTokenProvider()));
        // Hand audio focus to ExoPlayer so playback ducks/pauses for a call or nav prompt and resumes after,
        // and pauses when headphones unplug (Phase 8 — none of this was configured before).
        var audioAttributes = new AudioAttributes.Builder()
            .SetUsage(C.UsageMedia)!
            .SetContentType(C.AudioContentTypeMusic)!
            .Build()!;
        player = new ExoPlayerBuilder(this)
            .SetMediaSourceFactory(new DefaultMediaSourceFactory(resolvingFactory))
            .SetAudioAttributes(audioAttributes, true)
            .SetHandleAudioBecomingNoisy(true)
            .Build()!;
        var tree = new MediaBrowseTree(LoadCatalog());
        mediaSession = new MediaLibrarySession.Builder(this, player, new LibraryCallback(tree)).Build();

        equalizerStore = new EqualizerStore(this);
        ApplyAudioEffects();

        handler = new Handler(Looper.MainLooper!);
        recorder = new Java.Lang.Runnable(RecordTick);
        handler.PostDelayed(recorder, RecordIntervalMs);
    }

    // Bind the saved equalizer curve + loudness boost to the player's current audio session. Cheap to call
    // repeatedly — AudioEffects only re-attaches when the session id actually changes.
    private void ApplyAudioEffects()
    {
        if (player is null || equalizerStore is null)
        {
            return;
        }

        var settings = equalizerStore.LoadLibrary()?.Active ?? new EqualizerSettings(false, []);
        audioEffects.Apply(player.AudioSessionId, settings, equalizerStore.LoudnessMillibels);
    }

    // Covariant return (MediaLibrarySession, not MediaSession): Java's
    // MediaLibraryService.onGetSession returns MediaLibrarySession, so the generated
    // wrapper must too, or javac rejects it.
    public override MediaLibrarySession? OnGetSession(MediaSession.ControllerInfo? controllerInfo) => mediaSession;

    public override MediaLibrarySession? OnGetSessionFromMediaLibraryService(MediaSession.ControllerInfo? controllerInfo) =>
        mediaSession;

    private MusicCatalog LoadCatalog()
    {
        using var database = MusicDatabase.Open(this);
        return new MusicCatalog(database.Catalog.LoadAll());
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == AudioPlaybackCommands.ActionSetSkipSilence && player is not null)
        {
            player.SkipSilenceEnabled = intent.GetBooleanExtra(AudioPlaybackCommands.ExtraEnabled, false);
            return StartCommandResult.Sticky;
        }

        if (intent?.Action == AudioPlaybackCommands.ActionApplyAudioEffects)
        {
            ApplyAudioEffects();
            return StartCommandResult.Sticky;
        }

        return base.OnStartCommand(intent, flags, startId);
    }

    private void RecordTick()
    {
        RecordCurrentPosition();
        ApplyAudioEffects(); // keep effects bound as the audio session changes across tracks
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

        audioEffects.Release();
        mediaSession?.Player?.Release();
        mediaSession?.Release();
        mediaSession = null;
        player = null;
        base.OnDestroy();
    }

    // The session's library callback. Converting to MediaLibraryService makes the
    // service Android-Auto/Assistant capable structurally; the browsable content
    // tree (built from `tree`) is populated in a follow-up once the Guava
    // ListenableFuture binding is wired. Playback, lockscreen, and Bluetooth work
    // through the session regardless.
    private sealed class LibraryCallback(MediaBrowseTree tree)
        : Java.Lang.Object, MediaLibrarySession.ICallback
    {
        public MediaBrowseTree Tree { get; } = tree;
    }
}
