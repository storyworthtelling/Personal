using Com.Tidal.Sdk.Player;
using Com.Tidal.Sdk.Player.Common.Model;
using Com.Tidal.Sdk.Player.Playbackengine.Model;
using Kotlin.Coroutines;
using Xamarin.KotlinX.Coroutines.Flow;

namespace Score;

/// <summary>
/// Drives one TIDAL <see cref="Player"/>: issues Load/Play/Pause/Seek, exposes the
/// plain, synchronous state TIDAL's Query API offers for polling
/// (<see cref="PositionSeconds"/>/<see cref="DurationSeconds"/>/<see cref="State"/>),
/// and turns <c>playbackEngine.events</c> — a Kotlin <c>Flow</c> — into ordinary C#
/// callbacks.
///
/// Position and duration do <b>not</b> need the events bridge at all:
/// <c>Query.assetPosition</c>/<c>Query.playbackContext.duration</c> are plain
/// synchronous property getters (confirmed against TIDAL's real
/// <c>PlaybackEngine.kt</c>), so <see cref="ScorePlayback"/> can poll them the same
/// way it already polls ExoPlayer for Drive/podcast playback. What genuinely needs the
/// events Flow is the two things Query can't answer by polling: knowing the moment a
/// track <i>ends</i> (<see cref="TrackEnded"/>, for queue auto-advance — no
/// <c>Query</c> member tells you playback stopped because it finished versus because
/// something paused it) and a stream failure (<see cref="PlaybackError"/>, for
/// surfacing something instead of a track that silently never starts).
///
/// Reaching the Flow means calling <b>into</b> a real suspend function
/// (<c>Flow.collect</c>) — the harder coroutine-bridge direction this session avoided
/// everywhere else (device-code sign-in went through plain HTTP specifically to avoid
/// it). It turns out to need no new raw JNI, though: <c>IFlow.Collect</c>,
/// <c>IFlowCollector.Emit</c>, and <c>EmptyCoroutineContext.Instance</c> are all
/// already normal bound members (verified by reflecting the real
/// <c>Xamarin.KotlinX.Coroutines.Core.Jvm</c>/<c>Xamarin.Kotlin.StdLib</c> DLLs) —
/// implementing <see cref="IFlowCollector"/> is exactly the same shape as
/// <see cref="TidalCredentialsProvider"/> implementing <c>ICredentialsProvider</c>,
/// just simpler: <see cref="Emit"/> does its work synchronously (forward the event,
/// return), so unlike <c>GetCredentials</c> it never needs to return the
/// coroutine-suspended sentinel or complete a continuation later.
///
/// <c>Collect</c> itself is still a suspend call from this side — it only completes
/// when the flow itself completes or is cancelled, which for a hot, player-lifetime
/// flow like <c>events</c> should be "when the Player is released" — so it needs a
/// root <see cref="IContinuation"/> that this class owns and never expects to see
/// resumed under normal operation; if it ever is, that is itself a signal (the flow
/// ended or errored) worth logging loudly.
/// </summary>
public sealed class TidalPlaybackBridge : Java.Lang.Object, IFlowCollector
{
    private const string LogTag = "TidalPlaybackBridge";

    private readonly Player player;

    public TidalPlaybackBridge(Player player)
    {
        this.player = player;
        StartCollectingEvents();
    }

    /// <summary>Fires when the active track finishes playing on its own — the signal to advance the queue.</summary>
    public event Action? TrackEnded;

    /// <summary>Fires on a stream failure. The message is TIDAL's own, for logging/surfacing as-is.</summary>
    public event Action<string>? PlaybackError;

    /// <summary>Fires whenever TIDAL's own play/pause/buffering state changes.</summary>
    public event Action<PlaybackState>? StateChanged;

    public void Load(string productId) => player.PlaybackEngine.Load(TrackProduct(productId));

    public void SetNext(string? productId) =>
        player.PlaybackEngine.SetNext(productId is null ? null : TrackProduct(productId));

    public void Play() => player.PlaybackEngine.Play();

    public void Pause() => player.PlaybackEngine.Pause();

    public void Seek(float seconds) => player.PlaybackEngine.Seek(seconds);

    public void SkipToNext() => player.PlaybackEngine.SkipToNext();

    public void Release() => player.PlaybackEngine.Release();

    /// <summary>Seconds into the active track. Polled, not pushed — see the class remarks.</summary>
    public float PositionSeconds => player.PlaybackEngine.AssetPosition;

    /// <summary>Seconds long the active track is, or null if nothing is loaded yet.</summary>
    public float? DurationSeconds => player.PlaybackEngine.PlaybackContext?.Duration;

    public PlaybackState State => player.PlaybackEngine.PlaybackState!;

    public string? ActiveProductId => player.PlaybackEngine.MediaProduct?.ProductId;

    private static MediaProduct TrackProduct(string productId) =>
        new(ProductType.Track!, productId, sourceType: null, sourceId: null, extras: null, referenceId: null);

    private void StartCollectingEvents()
    {
        Android.Util.Log.Debug(LogTag, "Collecting playbackEngine.Events...");
        try
        {
            var result = player.PlaybackEngine.Events.Collect(this, new RootContinuation(LogTag));
            Android.Util.Log.Debug(LogTag, $"Events.Collect(...) returned {result} synchronously " +
                "(expected: the coroutine-suspended sentinel — a hot, player-lifetime flow should not " +
                "complete this early).");
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning(LogTag, $"Events.Collect(...) threw: {e}");
        }
    }

    public Java.Lang.Object Emit(Java.Lang.Object? value, IContinuation p1)
    {
        try
        {
            HandleEvent(value);
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning(LogTag, $"Handling a playback event threw: {e}");
        }

        // Synchronous, so this never suspends — Unit.Instance is the normal
        // "completed immediately" return, not a Kotlin-defaults guess: Kotlin's own
        // generated Java-interop shims for a suspend function returning Unit use the
        // exact same singleton.
        return Kotlin.Unit.Instance!;
    }

    private void HandleEvent(Java.Lang.Object? value)
    {
        Android.Util.Log.Debug(LogTag, $"Event: {value}");

        // IEvent.Error extends Java.Lang.Throwable (so it can be thrown/caught as a
        // normal .NET exception), which in this binding is a sibling of
        // Java.Lang.Object rather than a subclass of it — a single instance can't
        // statically be both, so matching against the emitted Java.Lang.Object
        // straight away would make the Error case unreachable. Going through the
        // IEvent interface first (implemented by both branches) sidesteps that.
        if (value is not IEvent evt)
        {
            Android.Util.Log.Warn(LogTag, $"Emitted value is not an IEvent: {value}");
            return;
        }

        switch (evt)
        {
            case IEvent.MediaProductEnded:
                TrackEnded?.Invoke();
                break;
            case IEvent.Error error:
                PlaybackError?.Invoke(error.Message ?? error.GetType().Name);
                break;
            case IEvent.PlaybackStateChange stateChange:
                StateChanged?.Invoke(stateChange.PlaybackState!);
                break;
        }
    }

    /// <summary>
    /// The root continuation <see cref="Events"/>'s <c>collect</c> call resumes when
    /// the flow itself completes or is cancelled — see the class remarks for why that
    /// should not normally happen for a player-lifetime hot flow.
    /// </summary>
    private sealed class RootContinuation(string logTag) : Java.Lang.Object, IContinuation
    {
        public ICoroutineContext Context => Kotlin.Coroutines.EmptyCoroutineContext.Instance!;

        public void ResumeWith(Java.Lang.Object result) =>
            Android.Util.Log.Warn(logTag, $"Events.Collect's root continuation resumed with {result} — " +
                "the events flow ended or was cancelled, which should not happen while the Player is alive.");
    }
}
