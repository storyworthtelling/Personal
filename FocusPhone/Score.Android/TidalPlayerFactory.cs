using Android.App;
using Android.Content;
using AndroidX.Media3.Database;
using AndroidX.Media3.DataSource.Cache;
using Com.Tidal.Sdk.Player;
using Com.Tidal.Sdk.Player.Common.Model;
using Com.Tidal.Sdk.Player.Playbackengine.Model;
using Com.Tidal.Sdk.Player.Playbackengine.Player;
using Com.Tidal.Sdk.Player.Playbackengine.Playbackprivilege;
using Com.Tidal.Sdk.Player.Streamingapi;
using Codex.Infrastructure.Music;
using Square.OkHttp3;

namespace Score;

/// <summary>
/// Always grants online playback — Score has no separate entitlement/offline-rights
/// concept for TIDAL streams today, so there is nothing for this to differentiate.
/// TIDAL's own default implementation (see <c>Player.kt</c>'s default parameter) does
/// exactly the same thing; this exists only because a Kotlin object-expression default
/// isn't something a JVM constructor call from C# can reach.
/// </summary>
internal sealed class AlwaysOnlinePlaybackPrivilegeProvider : Java.Lang.Object, IPlaybackPrivilegeProvider
{
    public PlaybackPrivilege Get(MediaProduct mediaProduct) => PlaybackPrivilege.OkOnline!;
}

/// <summary>
/// Builds a real <see cref="Player"/> instance — the last piece <c>Player</c>'s
/// constructor needs beyond <see cref="TidalCredentialsProvider"/>/
/// <see cref="TidalEventSender"/>.
///
/// Every default value below is copied from TIDAL's actual Kotlin source
/// (<c>player/src/main/kotlin/com/tidal/sdk/player/Player.kt</c> and the three config
/// data classes it defaults, read from the real
/// <c>github.com/tidal-music/tidal-sdk-android</c> tree at the time this was written —
/// not invented), because the constructor bound in <c>Codex.Tidal.Binding</c> has no
/// Java-visible short overload: <c>javap</c> on the actual 0.0.71 <c>.aar</c> shows
/// exactly one 17-parameter constructor, so every one of Kotlin's defaults has to be
/// supplied explicitly. Three of those defaults (<see cref="BufferConfiguration"/>,
/// <see cref="AssetTimeoutConfig"/>, <see cref="StreamingApiTimeoutConfig"/>) are
/// themselves Kotlin data classes whose only public constructor takes raw
/// <c>Duration</c> longs, which is why building them goes through
/// <see cref="TidalKotlinBridge.NewDurationDataClass{T}"/> rather than a normal `new`.
///
/// <see cref="CacheProvider"/> deliberately uses <c>.External</c>, not TIDAL's own
/// default <c>.Internal()</c> — <c>Internal</c>'s constructor takes a <c>ByteAmount</c>,
/// another Kotlin inline value class with the same raw-encoding problem as
/// <c>Duration</c> but with no equivalent public "raw value from a plain number"
/// converter found on the bound stdlib surface, so replicating it would mean guessing
/// an internal packing scheme rather than calling into Kotlin's own conversion — a
/// materially different, worse kind of unverified than everything else in this file.
/// <c>External</c> sidesteps that entirely: it is a normal, unmangled, already-bound
/// constructor over a plain <see cref="AndroidX.Media3.DataSource.Cache.ICache"/>, so
/// this hands it a dedicated <see cref="SimpleCache"/> (its own "tidal" folder,
/// separate from <see cref="Downloads"/>' own cache so TIDAL's cache management can
/// never evict or overwrite something the owner explicitly downloaded) bounded to the
/// same 2 GB TIDAL's own default caps <c>Internal</c> at.
/// </summary>
internal static class TidalPlayerFactory
{
    private const string LogTag = "TidalPlayerFactory";
    private const string CacheFolder = "tidal";
    private const long CacheSizeBytes = 2_000_000_000L; // matches CacheProvider.Internal's own 2.gigabytes default

    public static Player? Create(
        Application application, TidalCredentialsProvider credentialsProvider, string appVersion)
    {
        Android.Util.Log.Debug(LogTag, "Building Player config objects via raw JNI...");

        if (TidalKotlinBridge.NewDurationDataClass<BufferConfiguration>(
                "com/tidal/sdk/player/playbackengine/model/BufferConfiguration",
                "(JJJJJJLkotlin/jvm/internal/DefaultConstructorMarker;)V",
                20_000L,  // backBufferDuration: 20.seconds
                120_000L, // minPlaybackBuffer: 2.minutes
                120_000L, // maxPlaybackBuffer: 2.minutes
                2_500L,   // bufferForPlayback: 2.5.seconds
                5_000L,   // bufferForPlaybackAfterRebuffer: 5.seconds
                1_500L)   // audioTrackBuffer: 1.5.seconds
            is not { } bufferConfiguration)
        {
            AudioDiagnostics.LogWarning(LogTag, "BufferConfiguration construction failed; cannot build Player.");
            return null;
        }

        if (TidalKotlinBridge.NewDurationDataClass<AssetTimeoutConfig>(
                "com/tidal/sdk/player/playbackengine/model/AssetTimeoutConfig",
                "(JJJLkotlin/jvm/internal/DefaultConstructorMarker;)V",
                15_000L, 30_000L, 15_000L) // connect/read/write, matching TIDAL's own defaults
            is not { } assetTimeoutConfig)
        {
            AudioDiagnostics.LogWarning(LogTag, "AssetTimeoutConfig construction failed; cannot build Player.");
            return null;
        }

        if (TidalKotlinBridge.NewDurationDataClass<StreamingApiTimeoutConfig>(
                "com/tidal/sdk/player/streamingapi/StreamingApiTimeoutConfig",
                "(JJJLkotlin/jvm/internal/DefaultConstructorMarker;)V",
                15_000L, 30_000L, 15_000L) // connect/read/write, matching TIDAL's own defaults
            is not { } streamingApiTimeoutConfig)
        {
            AudioDiagnostics.LogWarning(LogTag, "StreamingApiTimeoutConfig construction failed; cannot build Player.");
            return null;
        }

        var cache = new SimpleCache(
            new Java.IO.File(application.FilesDir, CacheFolder),
            new LeastRecentlyUsedCacheEvictor(CacheSizeBytes),
            new StandaloneDatabaseProvider(application));
        var cacheProvider = new CacheProvider.External(cache);

        try
        {
            Android.Util.Log.Debug(LogTag, "Constructing Player...");
            var player = new Player(
                application,
                credentialsProvider,
                new TidalEventSender(),
                enableDecoderFallback: true,
                userClientIdSupplier: null,
                bufferConfiguration,
                assetTimeoutConfig,
                streamingApiTimeoutConfig,
                cacheProvider,
                isOfflineMode: false,
                isDebuggable: false,
                new OkHttpClient.Builder().Build()!,
                new AlwaysOnlinePlaybackPrivilegeProvider(),
                offlinePlayProvider: null,
                appVersion,
                Com.Tidal.Sdk.Player.Common.Common.TidalApiEndpointV1,
                Com.Tidal.Sdk.Player.Common.Common.TidalOpenApiEndpointV2);
            Android.Util.Log.Debug(LogTag, "Player constructed.");
            return player;
        }
        catch (Exception e)
        {
            AudioDiagnostics.LogWarning(LogTag, $"new Player(...) threw: {e}");
            return null;
        }
    }
}
