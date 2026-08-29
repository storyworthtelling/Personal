using System.Net.Http;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.Session;
using Codex.Infrastructure.Music;
using Codex.Ui.Voice;
using Score.Google;
using Codex.Domain.Audio;
using Codex.Domain.Music;
using Storyworthtelling.Voice.Secrets;

namespace Score;

/// <summary>
/// Background podcast playback: a MediaSessionService hosting ExoPlayer, which
/// provides the media notification, lockscreen controls, and audio focus. It also
/// owns the resume book — a lightweight main-thread poll records the current
/// position into <see cref="PlaybackProgressStore"/> so playback resumes exactly
/// even when listening happens entirely in the background. Skip-silence (an
/// ExoPlayer setting not reachable through MediaController) is toggled via an
/// Intent from the activity.
///
/// Also hosts a second, independent <see cref="MediaLibrarySession"/> for TIDAL
/// streams, wrapping <see cref="TidalMediaSessionPlayer"/> instead of ExoPlayer — see
/// the design note above <see cref="DriveSessionId"/>. <c>MediaLibraryService</c>'s
/// covariant <c>OnGetSession</c> override requires returning a
/// <c>MediaLibrarySession</c> specifically (not a plain <c>MediaSession</c>), which is
/// why the TIDAL side is a second <c>MediaLibrarySession</c> too rather than a lighter
/// plain session — this single service can only ever hand back that one type.
/// </summary>
[Service(
    Name = "com.storyworthtelling.focusphone.audio.PlaybackService",
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(["androidx.media3.session.MediaLibraryService"])]
public class PlaybackService : MediaLibraryService
{
    private const long RecordIntervalMs = 10_000;

    /// <summary>The connection-hint value (see <see cref="ConnectionHintSessionKey"/>) and
    /// <see cref="AudioPlaybackCommands.ExtraSessionId"/> value naming the Drive/podcast session.</summary>
    public const string DriveSessionId = "drive";

    /// <summary>The connection-hint value and <see cref="AudioPlaybackCommands.ExtraSessionId"/>
    /// value naming the TIDAL session.</summary>
    public const string TidalSessionId = "tidal";

    /// <summary>
    /// The <c>MediaController.Builder.SetConnectionHints</c> key <see cref="ScorePlayback"/> sets
    /// to <see cref="DriveSessionId"/> or <see cref="TidalSessionId"/> to reach a specific one of
    /// this service's two sessions — an ordinary <c>SessionToken(context, componentName)</c> only
    /// names the service, not which of several sessions inside it a controller wants, so
    /// <see cref="OnGetSession"/> reads this out of <c>ControllerInfo.ConnectionHints</c> instead.
    /// A controller that connects without this hint at all — the lockscreen, Bluetooth, Android
    /// Auto, none of which know this service hosts two sessions — falls back to
    /// <see cref="activeSessionId"/>, so whichever source is actually playing is what those
    /// surfaces see.
    /// </summary>
    private const string ConnectionHintSessionKey = "score.session";

    private MediaLibrarySession? driveMediaSession;
    private MediaLibrarySession? tidalMediaSession;
    private TidalPlaybackBridge? tidalBridge;
    private IExoPlayer? player;
    private PlaybackProgressStore? progressStore;
    private PlaybackProgressBook book = new();
    private Handler? handler;
    private Java.Lang.Runnable? recorder;
    private EqualizerStore? equalizerStore;
    private readonly AudioEffects audioEffects = new();

    // Which session OnGetSession hands back to a controller that connects without naming one —
    // see ConnectionHintSessionKey. Defaults to Drive: until something has actually started TIDAL
    // playback, the lockscreen/Bluetooth/Auto should see the session that might have something in
    // it, not the one that provably doesn't yet.
    private string activeSessionId = DriveSessionId;

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

        // Read through the download cache: anything downloaded plays from disk and
        // needs no network, and everything else streams exactly as before. It is
        // read-only, so streaming a track can never quietly fill the download budget
        // with something nobody asked to keep.
        var readFactory = Downloads.ReadFactory(this, resolvingFactory);
        // Hand audio focus to ExoPlayer so playback ducks/pauses for a call or nav prompt and resumes after,
        // and pauses when headphones unplug (Phase 8 — none of this was configured before).
        var audioAttributes = new AudioAttributes.Builder()
            .SetUsage(C.UsageMedia)!
            .SetContentType(C.AudioContentTypeMusic)!
            .Build()!;
        player = new ExoPlayerBuilder(this)
            .SetMediaSourceFactory(new DefaultMediaSourceFactory(readFactory))
            .SetAudioAttributes(audioAttributes, true)
            .SetHandleAudioBecomingNoisy(true)
            .Build()!;
        var tree = new MediaBrowseTree(LoadCatalog());
        driveMediaSession = new MediaLibrarySession.Builder(this, player, new LibraryCallback(tree))
            .SetId(DriveSessionId)!
            .Build();

        equalizerStore = new EqualizerStore(this);
        ApplyAudioEffects();

        handler = new Handler(Looper.MainLooper!);
        recorder = new Java.Lang.Runnable(RecordTick);
        handler.PostDelayed(recorder, RecordIntervalMs);

        // Best-effort, off the main thread, matching MusicLibrary.RestoreFolderFromVault: there is
        // no stored TIDAL account today (no pairing screen has ever produced one — see
        // TidalAccountStore), so this quietly leaves tidalMediaSession null and the service behaves
        // exactly as it did before tonight.
        Task.Run(() => BuildTidalSessionAsync(tree));
    }

    /// <summary>
    /// Builds the second session, if there is a TIDAL account to build it for. Every step degrades
    /// quietly (log and return) rather than throwing, the same rule every other vault-backed feature
    /// in this app follows — a missing account, an unreachable vault, or a rejected refresh token are
    /// all "not signed in today", not errors.
    /// </summary>
    private async Task BuildTidalSessionAsync(MediaBrowseTree tree)
    {
        const string logTag = "PlaybackService.Tidal";
        var account = new TidalAccountStore(this).Load();
        if (account is null)
        {
            Android.Util.Log.Debug(logTag, "No stored TIDAL account; skipping the TIDAL session.");
            return;
        }

        var vaultAccess = new SecretsVaultAccess(new DeviceAccountTokenProvider().GetAccessToken, "Score");
        var vaultResult = await vaultAccess.Service.LoadAsync(CancellationToken.None);
        if (vaultResult.Status != SecretsVaultStatus.Loaded || string.IsNullOrEmpty(vaultResult.Vault.Tidal.ClientId))
        {
            Android.Util.Log.Warn(logTag, $"Vault not readable or Tidal.ClientId unset (status={vaultResult.Status}); " +
                "skipping the TIDAL session.");
            return;
        }

        var clientId = vaultResult.Vault.Tidal.ClientId;
        var clientSecret = vaultResult.Vault.Tidal.ClientSecret;

        // Called by TidalCredentialsProvider each time the SDK asks for fresh credentials, not just
        // once here — refreshes on demand over the Player's lifetime, the same contract
        // ICredentialsProvider.GetCredentials documents.
        Task<TidalTokenPollResult> RefreshAccessToken()
        {
            var current = new TidalAccountStore(this).Load();
            if (current is null)
            {
                return Task.FromResult(new TidalTokenPollResult(TidalTokenPollStatus.Denied));
            }

            return new TidalDeviceAuthClient(new HttpClient())
                .RefreshAsync(clientId, clientSecret, current.RefreshToken, CancellationToken.None);
        }

        var probe = await RefreshAccessToken();
        if (probe.Status != TidalTokenPollStatus.Success)
        {
            Android.Util.Log.Warn(logTag, $"Stored TIDAL refresh token did not exchange (status={probe.Status}); " +
                "skipping the TIDAL session.");
            return;
        }

        // A rotated refresh token invalidates the old one on TIDAL's side, so the new one has to be
        // saved immediately — not just used for this one call — or the next refresh fails.
        if (!string.IsNullOrEmpty(probe.RefreshToken))
        {
            new TidalAccountStore(this).Save(new TidalAccount(account.UserId, probe.RefreshToken));
        }

        var credentialsProvider = new TidalCredentialsProvider(RefreshAccessToken);
        var tidalPlayer = TidalPlayerFactory.Create(Application!, credentialsProvider, appVersion: "1.0.0");
        if (tidalPlayer is null)
        {
            Android.Util.Log.Error(logTag, "TidalPlayerFactory.Create returned null; skipping the TIDAL session.");
            return;
        }

        // Session/player construction has to happen on the application looper's thread — everything
        // else in this service already runs on the main thread by Service-lifecycle default, so this
        // is the one place that has to hop back to it explicitly, having done the async vault/network
        // work off it.
        var ready = new TaskCompletionSource();
        handler?.Post(() =>
        {
            try
            {
                var bridge = new TidalPlaybackBridge(tidalPlayer);
                var sessionPlayer = new TidalMediaSessionPlayer(Looper.MainLooper!, bridge);
                tidalBridge = bridge;
                tidalMediaSession = new MediaLibrarySession.Builder(this, sessionPlayer, new LibraryCallback(tree))
                    .SetId(TidalSessionId)!
                    .Build();
                Android.Util.Log.Debug(logTag, "TIDAL session built.");
            }
            catch (Exception e)
            {
                Android.Util.Log.Error(logTag, $"Building the TIDAL session threw: {e}");
            }
            finally
            {
                ready.SetResult();
            }
        });
        await ready.Task;
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
    public override MediaLibrarySession? OnGetSession(MediaSession.ControllerInfo? controllerInfo) =>
        ResolveSession(controllerInfo);

    public override MediaLibrarySession? OnGetSessionFromMediaLibraryService(MediaSession.ControllerInfo? controllerInfo) =>
        ResolveSession(controllerInfo);

    // See ConnectionHintSessionKey: a controller that asked for a specific session by hint gets it
    // (falling back to Drive if the TIDAL session hasn't finished building yet — Drive always
    // exists by the time any controller can connect); one that didn't ask gets whichever source is
    // actually active.
    private MediaLibrarySession? ResolveSession(MediaSession.ControllerInfo? controllerInfo)
    {
        var hint = controllerInfo?.ConnectionHints?.GetString(ConnectionHintSessionKey);
        return (hint ?? activeSessionId) switch
        {
            TidalSessionId => tidalMediaSession ?? driveMediaSession,
            _ => driveMediaSession,
        };
    }

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

        if (intent?.Action == AudioPlaybackCommands.ActionSetActiveSession)
        {
            var sessionId = intent.GetStringExtra(AudioPlaybackCommands.ExtraSessionId);
            if (sessionId is DriveSessionId or TidalSessionId)
            {
                activeSessionId = sessionId;
            }

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
        driveMediaSession?.Player?.Release();
        driveMediaSession?.Release();
        driveMediaSession = null;
        player = null;

        tidalMediaSession?.Player?.Release();
        tidalMediaSession?.Release();
        tidalMediaSession = null;
        tidalBridge = null;

        base.OnDestroy();
    }

    // The session's library callback. Converting to MediaLibraryService makes the service
    // Android-Auto/Assistant capable structurally, and the browsable content it would serve is the
    // unit-tested MediaBrowseTree (root -> Albums/Artists/Songs -> tracks). Actually answering
    // OnGetLibraryRoot/OnGetChildren needs the Guava ListenableFuture binding, which isn't referenced in
    // this head yet (Com.Google.Common.Util.Concurrent doesn't resolve) — wiring it needs the
    // Xamarin.Google.Guava.ListenableFuture package matched to Media3.Session, plus a car/DHU to verify.
    // Deferred as an on-device follow-up; the tree is ready. Playback, lockscreen, and Bluetooth work
    // through the session regardless.
    private sealed class LibraryCallback(MediaBrowseTree tree)
        : Java.Lang.Object, MediaLibrarySession.ICallback
    {
        public MediaBrowseTree Tree { get; } = tree;
    }
}
