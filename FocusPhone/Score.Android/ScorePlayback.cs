using Android.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using MediaController = AndroidX.Media3.Session.MediaController;

namespace Score;

/// <summary>One line of the live queue: what it is, and whether it is the one playing.</summary>
public sealed record QueuedItem(string Title, string? Subtitle, bool IsCurrent);

/// <summary>What kind of thing the player was last asked to play.</summary>
public enum MediaKind
{
    None,
    Music,
    Podcast,
}

/// <summary>
/// Everything that talks to the media session, and the only thing that holds the
/// <see cref="MediaController"/>s.
///
/// It exists because both areas need the player and neither owns it: Music starts a
/// list of tracks, Podcasts starts a queue of episodes with a per-show speed and a
/// resume point, and the shell's ticker drives the now-playing bar from whatever is
/// current. Keeping the controllers here is what stops each of those reaching for a
/// field on the Activity.
///
/// It deliberately knows nothing about episodes, tracks, shows or catalogs — callers
/// hand it built <see cref="MediaItem"/>s. The one thing it does remember is
/// <see cref="Kind"/>, because the listening log must not count a podcast as music
/// and asking the podcast area "is something of yours playing?" would put that
/// knowledge back in the shell.
///
/// <see cref="PlaybackService"/> hosts two independent sessions — Drive/podcasts over
/// ExoPlayer, and TIDAL over <see cref="TidalMediaSessionPlayer"/> — because TIDAL's
/// SDK owns its own internal player that can never be handed to the existing session.
/// This class holds a <see cref="MediaController"/> for each and picks between them
/// per call, rather than exposing two controllers to every caller: everything reads
/// and writes through the one <see cref="Controller"/> a caller already expects, and
/// only <see cref="Start"/>/<see cref="FadeInto"/> — the calls that know what is about
/// to play — decide which session that means. A TIDAL <see cref="MediaItem"/> is
/// recognised by its id carrying Score's existing "tidal:" prefix (see
/// <c>TidalCatalogClient</c>), the same test <c>TidalMediaSessionPlayer</c> uses to
/// recover a TIDAL product id from a <c>MediaItem</c> — one convention, not two.
/// </summary>
public sealed class ScorePlayback(Activity host)
{
    private const string TidalIdPrefix = "tidal:";

    // A skip is 30 seconds. Podcast-shaped by origin — the workout and music screens
    // want their own steps — but this is the only skip the app has today.
    private const long SkipMs = 30_000;

    /// <summary>How long the fade around a change of run takes. Fixed: the length is
    /// not a thing anyone wants to set, and a dial for it would be a worse screen.</summary>
    private const int FadeMs = 700;

    private MediaController? driveController;
    private MediaController? tidalController;

    /// <summary>
    /// Whichever session last had something started on it — what every read
    /// (position, now-playing metadata, queue) and transport control (play/pause,
    /// skip, seek) actually talks to. Null only before <see cref="Start"/> has ever
    /// been called, in which case reads fall back to the Drive controller so a cold
    /// Playing screen still shows "nothing playing" rather than nothing at all.
    /// </summary>
    private MediaController? active;

    /// <summary>The controller everything reads/writes through — the active session
    /// once something has played, otherwise the Drive session.</summary>
    public MediaController? Controller => active ?? driveController;

    /// <summary>What was last started. None until something plays.</summary>
    public MediaKind Kind { get; private set; }

    /// <summary>Whether what is currently active is a TIDAL stream rather than Drive/podcast content.</summary>
    public bool IsTidal => active is not null && active == tidalController;

    /// <summary>
    /// What the current item's playback settings are filed under — a podcast's feed
    /// id, null for music. It travels with the start rather than being looked up,
    /// so the Playing screen can offer per-show speed without having to know what a
    /// show is.
    /// </summary>
    public string? SettingsKey { get; private set; }

    public bool IsPlaying => Controller?.IsPlaying == true;

    public string? CurrentTitle => Controller?.MediaMetadata?.Title?.ToString();

    public string? CurrentArtist => Controller?.MediaMetadata?.Artist?.ToString();

    public string? CurrentAlbum => Controller?.MediaMetadata?.AlbumTitle?.ToString();

    public string? CurrentMediaId => Controller?.CurrentMediaItem?.MediaId;

    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, Controller?.CurrentPosition ?? 0));

    public TimeSpan Duration =>
        Controller?.Duration is { } ms && ms > 0 ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;

    /// <summary>
    /// Binds to the playback service's two sessions. Both controllers are built
    /// asynchronously, so they stay null for a moment after a cold start — callers
    /// check <see cref="Controller"/> rather than assuming.
    /// </summary>
    public void Connect()
    {
        var token = new SessionToken(host, new ComponentName(host, Java.Lang.Class.FromType(typeof(PlaybackService))));
        var executor = AndroidX.Core.Content.ContextCompat.GetMainExecutor(host);

        var driveFuture = new MediaController.Builder(host, token)
            .SetConnectionHints(SessionHint(PlaybackService.DriveSessionId))!
            .BuildAsync();
        driveFuture.AddListener(new Java.Lang.Runnable(() =>
        {
            driveController = (MediaController)driveFuture.Get()!;
            RunPending();
        }), executor);

        var tidalFuture = new MediaController.Builder(host, token)
            .SetConnectionHints(SessionHint(PlaybackService.TidalSessionId))!
            .BuildAsync();
        tidalFuture.AddListener(new Java.Lang.Runnable(() =>
        {
            tidalController = (MediaController)tidalFuture.Get()!;
        }), executor);
    }

    private readonly List<Action> pendingOnConnect = [];

    /// <summary>
    /// Runs the given action once a controller exists — immediately if one already does, or
    /// deferred until <see cref="Connect"/>'s asynchronously-built drive controller resolves.
    ///
    /// <see cref="Connect"/>'s own remarks warn that <see cref="Controller"/> stays null for a
    /// moment after a cold start; every caller that used to check for that and simply drop the
    /// request (a tap landing in that brief window) dropped it with no feedback at all — the
    /// bottom bar reads persisted queue/session state directly, so the request looked queued
    /// while the actual Play() call never went out, and nothing short of tapping Play again
    /// recovered it. Queuing here instead means the tap that started this call is honored
    /// exactly once the controller is ready, rather than silently lost.
    /// </summary>
    public void WhenReady(Action action)
    {
        if (Controller is not null)
        {
            action();
            return;
        }

        pendingOnConnect.Add(action);
    }

    private void RunPending()
    {
        if (pendingOnConnect.Count == 0)
        {
            return;
        }

        var actions = pendingOnConnect.ToList();
        pendingOnConnect.Clear();
        foreach (var action in actions)
        {
            action();
        }
    }

    private static Android.OS.Bundle SessionHint(string sessionId)
    {
        var bundle = new Android.OS.Bundle();
        bundle.PutString("score.session", sessionId);
        return bundle;
    }

    public void Release()
    {
        driveController?.Release();
        tidalController?.Release();
    }

    /// <summary>
    /// Starts a list at an index. Speed and skip-silence are set on every start
    /// rather than left as they were, so a podcast at 1.4× can't carry its speed
    /// into the next album. Picks Drive or TIDAL by the first item's id (see the
    /// class remarks) and pauses whichever session is not the one just started, so
    /// only one thing is ever actually playing — two active sessions would mean two
    /// competing audio-focus holders and two lockscreen cards fighting over one now-
    /// playing slot.
    /// </summary>
    public void Start(
        MediaKind kind, IList<MediaItem> items, int index, long startMs, float speed, bool skipSilence,
        string? settingsKey = null)
    {
        if (items.Count == 0)
        {
            return;
        }

        var startingTidal = items[0].MediaId?.StartsWith(TidalIdPrefix, StringComparison.Ordinal) == true;
        var controller = startingTidal ? tidalController : driveController;
        var idle = startingTidal ? driveController : tidalController;
        if (controller is null)
        {
            return;
        }

        active = controller;
        Kind = kind;
        SettingsKey = settingsKey;
        controller.SetMediaItems(items, index, startMs);
        controller.SetPlaybackSpeed(speed);
        controller.Prepare();
        controller.Play();
        SendSkipSilence(skipSilence);

        idle?.Pause();
        SetActiveSession(startingTidal ? PlaybackService.TidalSessionId : PlaybackService.DriveSessionId);
    }

    // Tells PlaybackService which session external surfaces (lockscreen, Bluetooth, Android Auto)
    // should be handed by default — see AudioPlaybackCommands.ActionSetActiveSession's own remarks
    // for why the service needs to be told rather than inferring it.
    private void SetActiveSession(string sessionId)
    {
        var intent = new Intent(host, typeof(PlaybackService));
        intent.SetAction(AudioPlaybackCommands.ActionSetActiveSession);
        intent.PutExtra(AudioPlaybackCommands.ExtraSessionId, sessionId);
        host.StartService(intent);
    }

    /// <summary>
    /// Applies a speed / skip-silence change to what is already playing, without
    /// restarting it. This is the per-show settings screen changing its mind mid-episode.
    /// </summary>
    /// <summary>
    /// Swaps what is playing for a new run, fading out and back in around the change.
    ///
    /// This is what a tempo change on the workout screen sounds like. It is a fade
    /// <b>through</b>, not an overlap: ExoPlayer plays one timeline at a time, and a
    /// true cross-fade needs a second player mixed against the first, which is a
    /// larger thing than the moment deserves. What it buys is that the change does not
    /// arrive as a cut, which is the part that breaks a stride.
    ///
    /// Workout tempo changes are always Drive content today, so this fades the
    /// currently-active controller specifically rather than assuming Drive — if that
    /// ever changes, the fade should still follow whatever <see cref="Start"/> is
    /// about to switch to.
    /// </summary>
    public void FadeInto(MediaKind kind, IList<MediaItem> items, int index)
    {
        if (Controller is not { } controller || items.Count == 0)
        {
            return;
        }

        Fade(controller, from: 1f, to: 0f, then: () =>
        {
            Start(kind, items, index, 0L, 1.0f, skipSilence: false);
            if (Controller is { } started)
            {
                Fade(started, from: 0f, to: 1f, then: null);
            }
        });
    }

    // Stepped on the main thread's handler: Media3's volume is a plain setter, so a
    // fade is a series of them rather than an animation the player runs itself.
    //
    // The whole sequence runs inside try/catch and logs through AudioDiagnostics rather than
    // letting anything escape: an exception thrown from a Handler.PostDelayed runnable is fatal
    // on Android (it crashes the whole app on the main thread) with nothing captured anywhere —
    // which would exactly match a workout tempo change that "stops the current track and never
    // starts the next one" with no trace of why, since a crash mid-fade-out leaves the controller
    // silenced (Volume already stepped toward 0) and the fade-in/Start() callback never runs.
    private static void Fade(MediaController controller, float from, float to, Action? then)
    {
        const int Steps = 12;
        var handler = new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!);
        for (var step = 0; step <= Steps; step++)
        {
            var at = step;
            handler.PostDelayed(() =>
            {
                try
                {
                    controller.Volume = from + ((to - from) * at / (float)Steps);
                    if (at == Steps)
                    {
                        then?.Invoke();
                    }
                }
                catch (Exception e)
                {
                    AudioDiagnostics.LogWarning("ScorePlayback.Fade",
                        $"Fade step {at}/{Steps} (from {from} to {to}) failed.", e);
                }
            }, (long)(FadeMs * at / (double)Steps));
        }
    }

    public void ApplySettings(float speed, bool skipSilence)
    {
        Controller?.SetPlaybackSpeed(speed);
        SendSkipSilence(skipSilence);
    }

    /// <summary>
    /// What the player is holding, in order, with the current item marked.
    ///
    /// Read from the live timeline rather than from either area's stored queue,
    /// because the timeline is by definition the medium that is playing — which is
    /// exactly what the Queue panel is meant to show. The stored queues exist to
    /// survive a restart; this exists to be looked at.
    /// </summary>
    public IReadOnlyList<QueuedItem> Queued()
    {
        if (Controller is not { } controller)
        {
            return [];
        }

        var current = controller.CurrentMediaItemIndex;
        var items = new List<QueuedItem>();
        for (var i = 0; i < controller.MediaItemCount; i++)
        {
            var item = controller.GetMediaItemAt(i);
            items.Add(new QueuedItem(
                item?.MediaMetadata?.Title?.ToString() ?? string.Empty,
                item?.MediaMetadata?.Artist?.ToString(),
                i == current));
        }

        return items;
    }

    /// <summary>Jumps to a position in what is already playing.</summary>
    public void PlayQueuedAt(int index)
    {
        if (Controller is { } controller && index >= 0 && index < controller.MediaItemCount)
        {
            controller.SeekTo(index, 0L);
            controller.Play();
        }
    }

    public void TogglePlayPause()
    {
        if (Controller is null)
        {
            return;
        }

        if (Controller.IsPlaying)
        {
            Controller.Pause();
        }
        else
        {
            Controller.Play();
        }
    }

    public void SkipBack() => Controller?.SeekTo(Math.Max(0, (Controller?.CurrentPosition ?? 0) - SkipMs));

    public void SkipForward() => Controller?.SeekTo((Controller?.CurrentPosition ?? 0) + SkipMs);

    // Skip-silence lives in the Drive session's ExoPlayer audio processor — TIDAL's
    // Player has no equivalent setting, so this only ever needs to reach the Drive
    // service instance regardless of which controller is currently active.
    private void SendSkipSilence(bool enabled)
    {
        var intent = new Intent(host, typeof(PlaybackService));
        intent.SetAction(AudioPlaybackCommands.ActionSetSkipSilence);
        intent.PutExtra(AudioPlaybackCommands.ExtraEnabled, enabled);
        host.StartService(intent);
    }
}
