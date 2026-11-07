using SkiaSharp;

namespace IconStudio;

/// <summary>
/// Writes the grain tiles and branding medallions into every consumer
/// (DESIGN.md §6, §13). The source is the owner's drawn mark at
/// Branding/owners-mark.png, write-if-absent, and every consumer medallion
/// re-derives from it. It is never a photograph: the repository is public and
/// each medallion copy ships in an APK. The old photo path, Branding/owner.png,
/// is gitignored and read by nothing.
/// </summary>
public static class BrandAssets
{
    private static readonly string[] GrainTargets =
    [
        "FocusPhone/Codex.Ui.Android/Resources/drawable-nodpi/paper_grain.png",
        // MAUI heads use the plain `Resources/` prefix, not Platforms/Android/Resources — see IconSpecs.
        "Storyboarder/Folio.Android/Resources/drawable-nodpi/paper_grain.png",
        "Foreword/Foreword.Android/Resources/drawable-nodpi/paper_grain.png",
        "Ledger/Ledger.Android/Resources/drawable-nodpi/paper_grain.png",
    ];

    private static readonly string[] MedallionTargets =
    [
        "Storyboarder/Folio.Infrastructure/Resources/owner.png",
        "Storyboarder/Folio.Android/Resources/drawable-nodpi/owner_medallion.png",
        "Foreword/Foreword.Android/Resources/drawable-nodpi/owner_medallion.png",
        "FocusPhone/Post.Android/Resources/drawable-nodpi/owner_medallion.png",
        "Ledger/Ledger.Android/Resources/drawable-nodpi/owner_medallion.png",
        // Added for Index P11 §S3: Flyleaf, Score and Plate never had a medallion target at all
        // (not merely an unwired asset, like Post/Folio/Foreword/Index were before D146's Settings
        // wiring) — the maker's mark could not have appeared on their Settings screens even if
        // someone had already added the <Image>, because IconStudio never wrote the file.
        "FocusPhone/Flyleaf.Android/Resources/drawable-nodpi/owner_medallion.png",
        "FocusPhone/Score.Android/Resources/drawable-nodpi/owner_medallion.png",
        "FocusPhone/Plate.Android/Resources/drawable-nodpi/owner_medallion.png",
    ];

    /// <summary>The drawn mark every medallion is made from, relative to the repo root.</summary>
    public const string OwnerSource = "Branding/owners-mark.png";

    public static IReadOnlyList<string> Write(string repoRoot)
    {
        var written = new List<string>();

        var ownerSource = Path.Combine(repoRoot, OwnerSource.Replace('/', Path.DirectorySeparatorChar));
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
