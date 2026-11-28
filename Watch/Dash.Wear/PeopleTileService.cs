using Android.App;
using Android.Content;
using AndroidX.Concurrent.Futures;
using AndroidX.Wear.Tiles;
using Google.Common.Util.Concurrent;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Domain.Design;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>
/// The People waiting tile (P14 B8, W-5.1.7, A72; mock: Tile: People waiting, Tile: nobody waiting), reached by swiping
/// from the face (A35). When the system asks for it, it asks Post who is waiting, as People waiting does, on the pool and
/// within People waiting's own bounds (<see cref="PostLink"/>), and answers with one picture of the dash and the names
/// (<see cref="TilePicture"/>); a tap anywhere opens People waiting. It holds nothing of its own but the last few pictures,
/// in memory, for the system to read by their version; a text arriving and People waiting answering ask it to be drawn
/// again (<see cref="Refresh"/>).
///
/// <para>Built with the Tiles library's own layout builders (<c>androidx.wear.tiles</c>), which the library keeps beside
/// ProtoLayout's: Microsoft's binding of ProtoLayout 1.4.2 has no <c>DimensionBuilders</c>, so none of its elements can be
/// given a size. The renderer reads the same layout either way.</para>
/// </summary>
// The Tiles library's own layout builders are marked obsolete in favour of ProtoLayout's, which this binding can't size (above):
// the choice is deliberate, so the warning is set aside here and nowhere else.
#pragma warning disable CS0618
[Service(Name = WatchIdentity.DashTileName, Exported = true, Permission = "com.google.android.wearable.permission.BIND_TILE_PROVIDER", Label = DashWords.PeopleWaiting, Icon = "@mipmap/ic_launcher")] // not copy: the platform's permission
[IntentFilter([TileService.ActionBindTileProvider])]
[MetaData("androidx.wear.tiles.PREVIEW", Resource = "@mipmap/ic_launcher")] // not copy: the Tiles library's own preview key
public sealed class PeopleTileService : TileService
{
    private const string PictureId = "people"; // not copy: the tile's one picture, by its resource id
    private const string TapId = "open"; // not copy: the tile's one tap, by its id
    private const int Kept = 4;

    private static readonly object Gate = new();
    private static readonly LinkedList<(string Version, byte[] Png, int Width, int Height)> Pictures = new();

    /// <summary>Asks the system to draw the tile again; the system decides when, and may wait a moment.</summary>
    public static void Refresh(Context context)
    {
        try
        {
            TileService.GetUpdater(context)!.RequestUpdate(Java.Lang.Class.FromType(typeof(PeopleTileService)));
        }
        catch (Java.Lang.Exception failure)
        {
            DashLog.Quiet("DASH the tile couldn’t be asked to draw again", failure);
        }
    }

    protected override IListenableFuture OnTileRequest(RequestBuilders.TileRequest? p0)
    {
        var device = p0?.DeviceConfiguration;
        var widthDp = device?.ScreenWidthDp ?? 0;
        var heightDp = device?.ScreenHeightDp ?? 0;
        var density = device?.ScreenDensity ?? 1;
        var context = ApplicationContext ?? this;
        return Answer(completer => Task.Run(() => PostLink.Shared.AskAsync(context, new DashPeopleAsk())).ContinueWith(asked =>
        {
            try
            {
                var face = asked.Result.Answer is DashPeople people
                    ? PeopleTile.Of(people)
                    : PeopleTile.Unreached(asked.Result.Trouble ?? DashTrouble.Unreadable);
                completer.Set(Tile(context, face, widthDp, heightDp, density));
            }
            catch (Exception failure)
            {
                DashLog.Quiet("DASH the tile couldn’t be drawn", failure);
                completer.SetException(new Java.Lang.IllegalStateException("The People waiting tile couldn’t be drawn.")); // not copy: for the platform's log
            }
        }, TaskScheduler.Default));
    }

    protected override IListenableFuture OnTileResourcesRequest(RequestBuilders.ResourcesRequest? requestParams) =>
        Answer(completer => completer.Set(PictureFor(requestParams?.Version ?? string.Empty)));

    // The picture the system asks for by the version the tile named; one let go meanwhile is no picture, and the system asks again.
    private static ResourceBuilders.Resources PictureFor(string version)
    {
        var resources = new ResourceBuilders.Resources.Builder().SetVersion(version)!;
        if (Held(version) is { } picture)
        {
            var image = new ResourceBuilders.InlineImageResource.Builder()
                .SetData(picture.Png)!
                .SetWidthPx(picture.Width)!
                .SetHeightPx(picture.Height)!
                .SetFormat(ResourceBuilders.ImageFormatUndefined)!
                .Build()!;
            resources.AddIdToImageMapping(PictureId, new ResourceBuilders.ImageResource.Builder().SetInlineResource(image)!.Build()!);
        }
        return resources.Build()!;
    }

    // A future the system waits on, settled by the androidx.concurrent.futures adapter, the library's public way to make one.
    private static IListenableFuture Answer(Action<CallbackToFutureAdapter.Completer> settle) =>
        CallbackToFutureAdapter.GetFuture(new Settling(settle))!;

    private sealed class Settling(Action<CallbackToFutureAdapter.Completer> settle) : Java.Lang.Object, CallbackToFutureAdapter.IResolver
    {
        public Java.Lang.Object? AttachCompleter(CallbackToFutureAdapter.Completer? p0)
        {
            if (p0 is not null)
            {
                settle(p0);
            }
            return new Java.Lang.String("People waiting tile"); // not copy: the future's name in the platform's own reports
        }
    }

    // One picture filling the round screen, its version a digest of what it shows; a tap anywhere opens People waiting.
    private static TileBuilders.Tile Tile(Context context, TileFace face, int widthDp, int heightDp, float density)
    {
        var width = Math.Max(1, (int)Math.Round(widthDp * density));
        var height = Math.Max(1, (int)Math.Round(heightDp * density));
        var now = WatchPaints.Now();
        var glyph = Srgb.Argb(WatchPaints.Glass(WatchApp.Dash, now), PeopleTile.GlyphAlpha(face));
        var version = PeopleTile.Version(face, glyph, width, height);
        Hold(version, TilePicture.Draw(context, face, width, height, now), width, height);

        var people = new ActionBuilders.AndroidActivity.Builder()
            .SetPackageName(WatchIdentity.DashPackage)!
            .SetClassName(WatchIdentity.DashPeopleActivityName)!
            .Build()!;
        var open = new ActionBuilders.LaunchAction.Builder().SetAndroidActivity(people)!.Build()!;
        var modifiers = new ModifiersBuilders.Modifiers.Builder()
            .SetClickable(new ModifiersBuilders.Clickable.Builder().SetId(TapId)!.SetOnClick(open)!.Build()!)!
            .SetSemantics(new ModifiersBuilders.Semantics.Builder().SetContentDescription(face.Description)!.Build()!)!
            .Build()!;
        var picture = new LayoutElementBuilders.Image.Builder()
            .SetResourceId(PictureId)!
            .SetWidth(DimensionBuilders.Dp(widthDp)!)!
            .SetHeight(DimensionBuilders.Dp(heightDp)!)!
            .SetModifiers(modifiers)!
            .Build()!;
        var root = new LayoutElementBuilders.Box.Builder()
            .SetWidth(DimensionBuilders.Expand()!)!
            .SetHeight(DimensionBuilders.Expand()!)!
            .AddContent(picture)!
            .Build()!;

        return new TileBuilders.Tile.Builder()
            .SetResourcesVersion(version)!
            .SetFreshnessIntervalMillis((long)PeopleTile.Freshness.TotalMilliseconds)!
            .SetTimeline(TimelineBuilders.Timeline.FromLayoutElement(root)!)!
            .Build()!;
    }

    private static void Hold(string version, byte[] png, int width, int height)
    {
        lock (Gate)
        {
            if (Pictures.Any(picture => picture.Version == version))
            {
                return;
            }
            Pictures.AddLast((version, png, width, height));
            while (Pictures.Count > Kept)
            {
                Pictures.RemoveFirst();
            }
        }
    }

    private static (string Version, byte[] Png, int Width, int Height)? Held(string version)
    {
        lock (Gate)
        {
            foreach (var picture in Pictures)
            {
                if (picture.Version == version)
                {
                    return picture;
                }
            }
            return null;
        }
    }
}
#pragma warning restore CS0618
