using Watch.Domain.Design;
using Watch.Domain.Kit;

namespace FaceStudio;

/// <summary>
/// Dash's launcher icon (P14 B4; WD §3, family A): the dash, glowing in Dash's glass, on a round black icon with a thin
/// rim, as the glyph studies draw it, at every launcher density. Drawn from the kit's own glyph geometry
/// (<see cref="GlyphGeometry"/>), so the icon and the glyph the app draws cannot drift apart (WD §13's parity rule). An
/// icon cannot follow the day, so it takes Dash's colour at the hour the mockups were drawn and approved at.
/// Committed beside the head, like the face, and held to this tool by <c>DashIconTests</c>.
/// </summary>
public static class DashIcon
{
    public const string Folder = "Watch/Dash.Wear/Resources";

    /// <summary>The hour every mockup is drawn at (screens-r1.html: "drawn here at 4:20 pm").</summary>
    public static readonly TimeOnly DrawnAt = new(16, 20);

    /// <summary>Android's launcher densities and their 48 dp icon's side in pixels.</summary>
    public static readonly IReadOnlyList<(string Density, int Side)> Densities =
        [("mdpi", 48), ("hdpi", 72), ("xhdpi", 96), ("xxhdpi", 144), ("xxxhdpi", 192)];

    private const string Name = "ic_launcher";

    public static IReadOnlyList<FaceFile> All() =>
        Densities.Select(entry => new FaceFile($"mipmap-{entry.Density}/{Name}.png", Draw(entry.Side).Png())).ToList();

    /// <summary>Dash's glass at <see cref="DrawnAt"/>, lifted as every Dash glyph is (<see cref="WatchTokens.GlyphLift"/>).</summary>
    public static Oklch Glass()
    {
        var colour = DayColour.At(WatchApp.Dash, DrawnAt);
        return colour with { L = Math.Clamp(colour.L + WatchTokens.GlyphLift(WatchApp.Dash), 0, 1) };
    }

    public static Raster Draw(int side)
    {
        var raster = new Raster(side, side);
        var centre = side / 2.0;
        raster.Paint((x, y) => Distance(x, y, centre, centre) <= centre, Srgb.Argb(WatchTokens.Ground));

        var rim = WatchTokens.Icon.RimStroke * side;
        raster.Paint((x, y) => Distance(x, y, centre, centre) is var from && from <= centre && from >= centre - rim, Srgb.Argb(WatchTokens.Icon.Rim));

        var glass = Glass();
        var half = GlyphGeometry.DashStroke * side / 2;
        var glow = GlyphGeometry.Glow * side;
        var start = GlyphGeometry.DashFrom * side;
        var end = GlyphGeometry.DashTo * side;
        raster.Paint(
            (x, y) => FromDash(x, y, start, end, centre) is var away && away > half && away < half + glow,
            (x, y) => Srgb.Argb(glass, WatchTokens.Icon.GlowAlpha * Math.Pow(1 - (FromDash(x, y, start, end, centre) - half) / glow, 2)));
        raster.Paint((x, y) => FromDash(x, y, start, end, centre) <= half, Srgb.Argb(glass));
        return raster;
    }

    /// <summary>Writes every density under <paramref name="repoRoot"/>'s <see cref="Folder"/> and removes any icon it no longer makes.</summary>
    public static void Write(string repoRoot)
    {
        var folder = Path.Combine(repoRoot, Folder);
        var files = All();
        var wanted = files.Select(file => Path.GetFullPath(Path.Combine(folder, file.Path))).ToHashSet();
        if (Directory.Exists(folder))
        {
            foreach (var stale in Directory.EnumerateFiles(folder, Name + "*", SearchOption.AllDirectories).Where(path => !wanted.Contains(Path.GetFullPath(path))))
            {
                File.Delete(stale);
            }
        }

        foreach (var file in files)
        {
            var path = Path.Combine(folder, file.Path);
            if (File.Exists(path) && FaceFiles.Same(file.Path, file.Bytes, File.ReadAllBytes(path)))
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.Bytes);
        }
    }

    private static double Distance(double x, double y, double toX, double toY) => Math.Sqrt((x - toX) * (x - toX) + (y - toY) * (y - toY));

    // How far a point is from the dash: a horizontal segment across the middle, so its round ends come for free.
    private static double FromDash(double x, double y, double start, double end, double middle) =>
        Distance(x, y, Math.Clamp(x, start, end), middle);
}
