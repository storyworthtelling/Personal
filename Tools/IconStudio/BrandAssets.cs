using SkiaSharp;

namespace IconStudio;

/// <summary>
/// Writes the grain tiles and branding medallions into every consumer
/// (DESIGN.md §6, §13). The owner source at Branding/owner.png is
/// write-if-absent: a real photo dropped there is never clobbered, and every
/// consumer medallion re-derives from it on the next run.
/// </summary>
public static class BrandAssets
{
    private static readonly string[] GrainTargets =
    [
        "FocusPhone/Codex.Ui.Android/Resources/drawable-nodpi/paper_grain.png",
        // MAUI heads use the plain `Resources/` prefix, not Platforms/Android/Resources — see IconSpecs.
        "Storyboarder/Storyboarder.Android/Resources/drawable-nodpi/paper_grain.png",
        "Foreword/Foreword.Android/Resources/drawable-nodpi/paper_grain.png",
        "Ledger/Ledger.Android/Resources/drawable-nodpi/paper_grain.png",
    ];

    private static readonly string[] MedallionTargets =
    [
        "Storyboarder/Storyboarder.Infrastructure/Resources/owner.png",
        "Storyboarder/Storyboarder.Android/Resources/drawable-nodpi/owner_medallion.png",
        "Foreword/Foreword.Android/Resources/drawable-nodpi/owner_medallion.png",
        "FocusPhone/Post.Android/Resources/drawable-nodpi/owner_medallion.png",
        "Ledger/Ledger.Android/Resources/drawable-nodpi/owner_medallion.png",
    ];

    public static IReadOnlyList<string> Write(string repoRoot)
    {
        var written = new List<string>();

        var ownerSource = Path.Combine(repoRoot, "Branding", "owner.png");
        if (!File.Exists(ownerSource))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ownerSource)!);
            using var serif = SKTypeface.FromFile(Path.Combine(repoRoot, "Fonts", "SourceSerif4-Semibold.ttf"))
                ?? throw new InvalidOperationException("Fonts/SourceSerif4-Semibold.ttf not found under repo root.");
            File.WriteAllBytes(ownerSource, BrandingMedallion.PlaceholderSource(serif));
            written.Add(ownerSource);
        }

        var medallion = BrandingMedallion.Medallion(File.ReadAllBytes(ownerSource));
        foreach (var target in MedallionTargets)
        {
            written.Add(WriteBytes(repoRoot, target, medallion));
        }

        var grain = GrainTile.RenderPng();
        foreach (var target in GrainTargets)
        {
            written.Add(WriteBytes(repoRoot, target, grain));
        }
        return written;
    }

    private static string WriteBytes(string repoRoot, string relativePath, byte[] bytes)
    {
        var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
