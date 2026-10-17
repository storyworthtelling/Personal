using SkiaSharp;

namespace IconStudio;

/// <summary>
/// Emits the complete adaptive-icon resource set for one IconSpec, following
/// the native pattern proven in Storyboarder (mipmap-anydpi-v26 XML + flat
/// paper background drawable + per-density foreground and legacy PNGs).
/// </summary>
public static class AdaptiveIconRenderer
{
    // (density qualifier, foreground px for the 108dp layer, legacy px for the 48dp icon)
    private static readonly (string Dpi, int ForegroundPx, int LegacyPx)[] Densities =
    [
        ("mdpi", 108, 48),
        ("hdpi", 162, 72),
        ("xhdpi", 216, 96),
        ("xxhdpi", 324, 144),
        ("xxxhdpi", 432, 192),
    ];

    public static IReadOnlyList<string> Write(string repoRoot, IconSpec spec)
    {
        var written = new List<string>();
        var root = Path.Combine(repoRoot, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar));

        var ground = LoadGround(repoRoot, spec);
        var plate = LoadPlate(repoRoot, spec);
        ValidateCrop(spec, plate);

        written.Add(WriteText(Path.Combine(root, "mipmap-anydpi-v26", $"{spec.Name}.xml"), AdaptiveXml(spec)));
        written.Add(WriteText(Path.Combine(root, "mipmap-anydpi-v26", $"{spec.Name}_round.xml"), AdaptiveXml(spec)));
        written.Add(WriteText(Path.Combine(root, "drawable", $"{spec.Name}_background.xml"), BackgroundXml(spec)));
        if (spec.WritesColorsXml)
        {
            written.Add(WriteText(Path.Combine(root, "values", "colors.xml"), ColorsXml(spec)));
        }

        foreach (var (dpi, foregroundPx, legacyPx) in Densities)
        {
            written.Add(WritePng(Path.Combine(root, $"mipmap-{dpi}", $"{spec.Name}_foreground.png"),
                RenderForeground(spec, foregroundPx, plate)));
            written.Add(WritePng(Path.Combine(root, $"mipmap-{dpi}", $"{spec.Name}.png"),
                RenderLegacy(spec, legacyPx, ground, plate)));

            if (ground is not null)
            {
                written.Add(WritePng(Path.Combine(root, $"mipmap-{dpi}", $"{spec.Name}_ground.png"),
                    RenderGround(ground, foregroundPx)));
            }
        }

        if (spec.CoverImage is not null)
        {
            written.Add(WritePng(
                Path.Combine(repoRoot, spec.CoverImage.Replace('/', Path.DirectorySeparatorChar)),
                RenderCover(spec, CoverPx, ground, plate)));
        }

        ground?.Dispose();
        plate?.Dispose();
        return written;
    }

    // Big enough for the shelf's 96dp-wide book at xxxhdpi without resampling artefacts,
    // small enough that the committed PNG stays sane next to the painted covers.
    private const int CoverPx = 512;

    // The shelf cover: the painted ground with the glyph on it, at natural scale.
    //
    // No safe-zone zoom, unlike the launcher icon — nothing masks this into a circle,
    // and Score's keyboard is drawn to run off both edges, so zooming would crop the
    // curve rather than protect it.
    private static byte[] RenderCover(IconSpec spec, int px, SKBitmap? ground, SKBitmap? plate)
    {
        using var surface = SKSurface.Create(new SKImageInfo(px, px));

        // A spec with PlateImage already HAS its window illustration: the painting is the
        // whole of what the shelf wants, and the shelf draws the bookplate around it. Baking
        // this spec's own keyline in would put a second one inside the shelf's, at a different
        // scale. So the art goes down alone — unless a spec opts in with CoverDrawsGlyph because its
        // Glyph draws something the shelf cannot supply. None does now: Index's corner flourishes
        // (D119) doubled the shelf's own brackets, so its cover is window only like the rest.
        if (plate is not null && !spec.CoverDrawsGlyph)
        {
            surface.Canvas.DrawBitmap(plate, SourceRect(spec, plate), new SKRect(0, 0, px, px));
            return Encode(surface);
        }

        if (ground is not null)
        {
            surface.Canvas.DrawBitmap(ground, new SKRect(0, 0, px, px));
        }
        else
        {
            surface.Canvas.Clear(SKColor.Parse(spec.BackgroundHex));
        }

        DrawPlate(surface.Canvas, spec, px, plate);
        spec.Glyph(surface.Canvas, px);
        return Encode(surface);
    }

    // The largest centred square of a bitmap, so art of any aspect fills a square window
    // without being stretched.
    // The spec's approved crop when it has one, else the centre square.
    private static SKRect SourceRect(IconSpec spec, SKBitmap plate) =>
        spec.Crop is { } crop
            ? new SKRect(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height)
            : CentreSquare(plate);

    // A crop that is not square, or reaches outside the painting, would stretch or smear edge
    // pixels into the icon without any error, so it fails here, naming the spec and the rectangle.
    private static void ValidateCrop(IconSpec spec, SKBitmap? plate)
    {
        if (spec.Crop is not { } crop)
        {
            return;
        }

        var rectangle = $"{crop.X},{crop.Y} {crop.Width}x{crop.Height}";
        if (plate is null)
        {
            throw new InvalidOperationException(
                $"{spec.App}: crop {rectangle} is set but the spec has no plate image to crop.");
        }

        var square = crop.Width > 0 && crop.Width == crop.Height;
        var inside = crop.X >= 0 && crop.Y >= 0
            && crop.X + crop.Width <= plate.Width && crop.Y + crop.Height <= plate.Height;
        if (!square || !inside)
        {
            throw new InvalidOperationException(
                $"{spec.App}: crop {rectangle} must be square and inside the {plate.Width}x{plate.Height} "
                + $"image {spec.PlateImage}.");
        }
    }

    private static SKRect CentreSquare(SKBitmap bitmap)
    {
        var side = Math.Min(bitmap.Width, bitmap.Height);
        return new SKRect(
            (bitmap.Width - side) / 2f, (bitmap.Height - side) / 2f,
            (bitmap.Width + side) / 2f, (bitmap.Height + side) / 2f);
    }

    public static string WritePreview(string directory, IconSpec spec, int size = 1024)
    {
        var info = new SKImageInfo(size, size);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Present the icon the way a launcher masks it: a rounded square.
        var radius = size * 0.22f;
        var rect = new SKRect(0, 0, size, size);
        using (var background = new SKPaint { Color = SKColor.Parse(spec.BackgroundHex), IsAntialias = true })
        {
            canvas.DrawRoundRect(rect, radius, radius, background);
        }
        // The adaptive foreground layer is 108dp with content in the central
        // safe zone; launchers show roughly the central 72dp. Zoom to match.
        const float zoom = 108f / 72f;
        canvas.Save();
        canvas.Translate(size / 2f, size / 2f);
        canvas.Scale(zoom);
        canvas.Translate(-size / 2f, -size / 2f);
        spec.Glyph(canvas, size);
        canvas.Restore();

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{spec.App}.png");
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static byte[] RenderForeground(IconSpec spec, int px, SKBitmap? plate = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(px, px));
        surface.Canvas.Clear(SKColors.Transparent);
        DrawPlate(surface.Canvas, spec, px, plate);
        spec.Glyph(surface.Canvas, px);
        return Encode(surface);
    }

    // §12's illustration window: cover art set INTO the plate rather than bleeding to the
    // edge, drawn before the glyph so the glyph's frame and corners sit over it. Square,
    // centre-cropped from whatever the painting's aspect happens to be.
    private static void DrawPlate(SKCanvas canvas, IconSpec spec, int px, SKBitmap? plate)
    {
        if (plate is null)
        {
            return;
        }

        var inset = px * spec.PlateInset;
        var window = new SKRect(inset, inset, px - inset, px - inset);
        canvas.DrawBitmap(plate, SourceRect(spec, plate), window);
    }

    // The cover art set into the window, if the spec has one.
    private static SKBitmap? LoadPlate(string repoRoot, IconSpec spec)
    {
        if (spec.PlateImage is null)
        {
            return null;
        }

        var path = Path.Combine(repoRoot, spec.PlateImage.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path)
            ? SKBitmap.Decode(path)
            : throw new FileNotFoundException($"{spec.App}'s plate image is missing.", path);
    }

    // The painted wash, if the spec has one. Null is the ordinary case: a flat
    // identity ground, which is a colour rather than a picture.
    private static SKBitmap? LoadGround(string repoRoot, IconSpec spec)
    {
        if (spec.GroundImage is null)
        {
            return null;
        }

        var path = Path.Combine(repoRoot, spec.GroundImage.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path)
            ? SKBitmap.Decode(path)
            : throw new FileNotFoundException($"{spec.App}'s ground image is missing.", path);
    }

    // The adaptive background layer. Filled edge to edge, because a launcher may
    // shift it against the foreground for parallax and any gap would show through.
    private static byte[] RenderGround(SKBitmap ground, int px)
    {
        using var surface = SKSurface.Create(new SKImageInfo(px, px));
        surface.Canvas.DrawBitmap(ground, new SKRect(0, 0, px, px));
        return Encode(surface);
    }

    private static byte[] RenderLegacy(IconSpec spec, int px, SKBitmap? ground = null, SKBitmap? plate = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(px, px));
        if (ground is not null)
        {
            surface.Canvas.DrawBitmap(ground, new SKRect(0, 0, px, px));
        }
        else
        {
            surface.Canvas.Clear(SKColor.Parse(spec.BackgroundHex));
        }

        // Legacy icons show the full square; enlarge the safe-zone artwork a little.
        const float zoom = 108f / 84f;
        var canvas = surface.Canvas;
        canvas.Translate(px / 2f, px / 2f);
        canvas.Scale(zoom);
        canvas.Translate(-px / 2f, -px / 2f);
        DrawPlate(canvas, spec, px, plate);
        spec.Glyph(canvas, px);
        return Encode(surface);
    }

    private static byte[] Encode(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string AdaptiveXml(IconSpec spec)
    {
        // A painted ground is a per-density bitmap; a flat one is the shape drawable.
        var background = spec.GroundImage is null
            ? $"@drawable/{spec.Name}_background"
            : $"@mipmap/{spec.Name}_ground";

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
              <background android:drawable="{background}" />
              <foreground android:drawable="@mipmap/{spec.Name}_foreground" />
            </adaptive-icon>
            """ + "\n";
    }

    private static string BackgroundXml(IconSpec spec) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <!-- Adaptive-icon background: flat paper (DESIGN.md §8). Generated by Tools/IconStudio. -->
        <shape xmlns:android="http://schemas.android.com/apk/res/android" android:shape="rectangle">
          <solid android:color="{spec.BackgroundHex}" />
        </shape>
        """ + "\n";

    private static string ColorsXml(IconSpec spec) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <!-- Generated by Tools/IconStudio; kept in step with DESIGN.md. -->
        <resources>
          <color name="appiconBackground">{spec.BackgroundHex}</color>
        </resources>
        """ + "\n";

    private static string WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string WritePng(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
