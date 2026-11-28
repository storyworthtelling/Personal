using Android.Content;
using Android.Graphics;
using Android.Text;
using Watch.Domain.Dash;
using Watch.Domain.Design;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>
/// The People waiting tile drawn as the mockup draws it (W-5.1.7, A72; mock: Tile: People waiting, Tile: nobody waiting):
/// true black, Dash's dash in its day colour, the newest name in full in the ink and the two before it fading in the grey,
/// each at its place in fractions of the radius (<see cref="DashLayout"/>). Drawn into a picture because a tile's own
/// text is set in the system's font, and the watch's words are IBM Plex Mono (WD §5). Nothing is written down: the
/// picture is held in memory for the system to read.
/// </summary>
internal static class TilePicture
{
    private const int Quality = 100; // not copy: PNG is lossless, and the platform reads the quality only for a lossy format

    public static byte[] Draw(Context context, TileFace face, int width, int height, TimeOnly now)
    {
        var radius = Math.Min(width, height) / 2f;
        using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(bitmap);
        canvas.DrawColor(WatchPaints.Of(WatchTokens.Ground));

        // The dash leads; with nobody waiting it sits faint at the centre, and nothing else is drawn but a quiet line.
        var glyphCentre = face.Names.Count > 0 ? DashLayout.TileGlyphCentre : DashLayout.TileNobodyCentre;
        GlyphPainter.Dash(canvas, width / 2f, (float)(glyphCentre * radius), (float)(DashLayout.TileGlyphSide * radius),
            WatchPaints.Glass(WatchApp.Dash, now), PeopleTile.GlyphAlpha(face));

        var lineWidth = (int)(DashLayout.TileNameWidth * radius);
        var pushed = 0f;
        foreach (var name in face.Names)
        {
            // The newest name in full: it wraps once rather than shrink (WD §5), and pushes the earlier two down by its
            // second line. The earlier two keep to one line each, cut at its end.
            var lines = name.Rank == TileRank.Newest ? 2 : 1;
            pushed += Words(canvas, Paint(context, name.Rank), name.Name, lines, lineWidth, width, (float)(DashLayout.TileNameCentre(name.Rank) * radius) + pushed);
        }
        if (face.Quiet is { } quiet)
        {
            using var soft = WatchPaints.Words(context, WatchType.Small, WatchWeight.Regular, WatchTokens.Neutral.Soft);
            Words(canvas, soft, quiet, 2, lineWidth, width, (float)(DashLayout.TileQuietCentre * radius));
        }

        using var png = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, Quality, png);
        return png.ToArray();
    }

    // The mockup's three names: the newest Glance in the ink, then Line and Small in the grey, fading.
    private static TextPaint Paint(Context context, TileRank rank) => rank switch
    {
        TileRank.Newest => WatchPaints.Words(context, WatchType.Glance, WatchWeight.SemiBold, WatchTokens.Neutral.Ink, PeopleTile.AlphaOf(rank)),
        TileRank.Earlier => WatchPaints.Words(context, WatchType.Line, WatchWeight.Medium, WatchTokens.Neutral.Soft, PeopleTile.AlphaOf(rank)),
        _ => WatchPaints.Words(context, WatchType.Small, WatchWeight.Regular, WatchTokens.Neutral.Soft, PeopleTile.AlphaOf(rank)),
    };

    // Words centred across the screen, their first line centred on the given height; how much further down a second line went.
    private static float Words(Canvas canvas, TextPaint paint, string words, int lines, int lineWidth, int width, float centre)
    {
        using var layout = StaticLayout.Builder.Obtain(words, 0, words.Length, paint, lineWidth)!
            .SetAlignment(Layout.Alignment.AlignCenter!)!
            .SetMaxLines(lines)!
            .SetEllipsize(TextUtils.TruncateAt.End)!
            .Build()!;
        var first = layout.GetLineBottom(0) - layout.GetLineTop(0);
        canvas.Save();
        canvas.Translate((width - lineWidth) / 2f, centre - first / 2f);
        layout.Draw(canvas);
        canvas.Restore();
        return layout.Height - first;
    }
}
