using Android.OS;
using AndroidX.Media3.Common;
using Google.Common.Util.Concurrent;

namespace Score;

/// <summary>
/// Wraps a <see cref="TidalPlaybackBridge"/> as an <c>androidx.media3.common.Player</c>
/// so it can back its own <c>MediaSession</c> — Score's Playing screen, the
/// lockscreen, and Bluetooth all talk to a <c>MediaController</c>, which only ever
/// talks to a real Player, never to <see cref="TidalPlaybackBridge"/> directly.
///
/// TIDAL's <c>Player</c> owns its own internal ExoPlayer and never exposes it, so
/// there is no existing <c>IPlayer</c> to hand the session — this has to build one.
/// Built on Media3's <c>SimpleBasePlayer</c> rather than implementing the ~100-member
/// <c>Player</c> interface directly: it is the sanctioned base class for exactly this
/// ("host a non-ExoPlayer engine behind a real Player/MediaSession"), needing only
/// <see cref="GetState"/> plus the handful of <c>Handle*</c> overrides for the
/// commands this actually supports.
///
/// Every <c>Handle*</c> override does its work synchronously (calling straight
/// through to <see cref="TidalPlaybackBridge"/>, which is itself synchronous — see
/// its own remarks) and returns <see cref="ImmediateFuture"/>, since there is no
/// asynchronous step to hand back a real pending future for.
/// </summary>
public sealed class TidalMediaSessionPlayer : SimpleBasePlayer
{
    private const string LogTag = "TidalMediaSessionPlayer";

    private readonly TidalPlaybackBridge bridge;
    private MediaItem? item;
    private bool playWhenReady;
    private bool ended;

    public TidalMediaSessionPlayer(Looper applicationLooper, TidalPlaybackBridge bridge) : base(applicationLooper)
    {
        this.bridge = bridge;
        bridge.TrackEnded += OnTrackEnded;
        bridge.StateChanged += _ => InvalidateState();
        bridge.PlaybackError += OnPlaybackError;
    }

    /// <summary>The active item's media id, or null when nothing is loaded — used by
    /// <see cref="PlaybackService"/> to know whether it is safe to idle this player
    /// when the other source becomes active.</summary>
    public string? ActiveMediaId => item?.MediaId;

    protected override State GetState()
    {
        var builder = new State.Builder()
            .SetAvailableCommands(new PlayerCommands.Builder().AddAllCommands().Build()!)!
            .SetPlayWhenReady(playWhenReady, BasePlayer.InterfaceConsts.PlayWhenReadyChangeReasonUserRequest)!
            .SetContentPositionMs((long)(bridge.PositionSeconds * 1000))!;

        if (item is not { } activeItem)
        {
            return builder.SetPlaybackState(BasePlayer.InterfaceConsts.StateIdle)!.Build()!;
        }

        builder.SetPlaybackState(ended ? BasePlayer.InterfaceConsts.StateEnded : BasePlayer.InterfaceConsts.StateReady);

        var durationUs = bridge.DurationSeconds is { } seconds
            ? (long)(seconds * 1_000_000)
            : global::AndroidX.Media3.Common.C.TimeUnset;
        var itemData = new MediaItemData.Builder(activeItem.MediaId!)
            .SetMediaItem(activeItem)!
            .SetDurationUs(durationUs)!
            .Build()!;
        builder.SetPlaylist(new List<MediaItemData> { itemData });

        return builder.Build()!;
    }

    protected override IListenableFuture HandlePrepare()
    {
        Android.Util.Log.Debug(LogTag, "HandlePrepare");
        if (item is { } activeItem)
        {
            bridge.Load(ProductId(activeItem));
        }

        return ImmediateFuture.Instance;
    }

    protected override IListenableFuture HandleSetPlayWhenReady(bool playWhenReady)
    {
        Android.Util.Log.Debug(LogTag, $"HandleSetPlayWhenReady({playWhenReady})");
        this.playWhenReady = playWhenReady;
        if (playWhenReady)
        {
            bridge.Play();
        }
        else
        {
            bridge.Pause();
        }

        InvalidateState();
        return ImmediateFuture.Instance;
    }

    protected override IListenableFuture HandleSeek(int mediaItemIndex, long positionMs, int seekCommand)
    {
        Android.Util.Log.Debug(LogTag, $"HandleSeek({positionMs}ms)");
        bridge.Seek(positionMs / 1000f);
        InvalidateState();
        return ImmediateFuture.Instance;
    }

    protected override IListenableFuture HandleSetMediaItems(
        IList<MediaItem> mediaItems, int startIndex, long startPositionMs)
    {
        item = mediaItems.Count > 0 ? mediaItems[Math.Clamp(startIndex, 0, mediaItems.Count - 1)] : null;
        ended = false;
        Android.Util.Log.Debug(LogTag, $"HandleSetMediaItems: active={item?.MediaId}");
        if (item is { } activeItem)
        {
            bridge.Load(ProductId(activeItem));
        }

        InvalidateState();
        return ImmediateFuture.Instance;
    }

    protected override IListenableFuture HandleStop()
    {
        Android.Util.Log.Debug(LogTag, "HandleStop");
        playWhenReady = false;
        bridge.Pause();
        InvalidateState();
        return ImmediateFuture.Instance;
    }

    protected override IListenableFuture HandleRelease()
    {
        Android.Util.Log.Debug(LogTag, "HandleRelease");
        bridge.Release();
        return ImmediateFuture.Instance;
    }

    private void OnTrackEnded()
    {
        Android.Util.Log.Debug(LogTag, "TrackEnded");
        ended = true;
        playWhenReady = false;
        InvalidateState();
    }

    private void OnPlaybackError(string message)
    {
        AudioDiagnostics.LogWarning(LogTag, $"PlaybackError: {message}");
        ended = true;
        InvalidateState();
    }

    // Score's TIDAL Track.Id carries a "tidal:{id}" prefix (see TidalCatalogClient) so
    // it can sit in the same catalog as Drive file ids without colliding; the SDK's
    // MediaProduct wants just the raw id.
    private static string ProductId(MediaItem mediaItem) =>
        mediaItem.MediaId is { } id && id.StartsWith("tidal:", StringComparison.Ordinal)
            ? id["tidal:".Length..]
            : mediaItem.MediaId ?? string.Empty;
}
