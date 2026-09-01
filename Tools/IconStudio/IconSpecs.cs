using SkiaSharp;

namespace IconStudio;

/// <param name="App">Display name, used in logs and preview filenames.</param>
/// <param name="ResourceRoot">Android resource root, relative to the repo root.</param>
/// <param name="Name">Base resource name (appicon or ic_launcher).</param>
/// <param name="WritesColorsXml">Whether to (re)write values/colors.xml with the paper background color.</param>
/// <param name="BackgroundHex">Adaptive-icon background color (default flat paper). Flyleaf's is
/// its warm identity ground, over which the foreground draws a cream bookplate cover.</param>
/// <param name="GroundImage">
/// A painted wash to use as the background layer instead of a flat colour, repo-relative
/// (DESIGN.md §12's hybrid recipe: baked wash, procedural grain, vector glyph). The flat
/// BackgroundHex stays set as the fallback and as the colour any XML consumer reads.
/// </param>
/// <param name="PlateImage">
/// Cover art set INTO the foreground as §12's illustration window, rather than filling the
/// background edge to edge. The difference is the whole ex-libris anatomy: a window has a
/// frame and corners around it, and art that bleeds to the edge has neither. Drawn before
/// the glyph, which draws the frame over it.
/// </param>
/// <param name="PlateInset">How far in from each edge the window sits, as a fraction of the layer.</param>
public sealed record IconSpec(
    string App,
    string ResourceRoot,
    string Name,
    bool WritesColorsXml,
    Action<SKCanvas, float> Glyph,
    string BackgroundHex = Tokens.PaperHex,
    string? GroundImage = null,
    string? PlateImage = null,
    float PlateInset = Glyphs.PlateInset);

public static class IconSpecs
{
    public static readonly IReadOnlyList<IconSpec> All =
    [
        new("FocusPhone.Launcher", "FocusPhone/Flyleaf.Android/Resources", "appicon", false, Glyphs.Bookplate, Tokens.FlyleafWarmHex),
        new("FocusPhone.Camera", "FocusPhone/FocusPhone.Camera.Android/Resources", "appicon", false, Glyphs.Lens),
        new("Score", "FocusPhone/Score.Android/Resources", "appicon", false, Glyphs.Keyboard,
            Tokens.ScoreGroundHex, GroundImage: "Branding/score-ground.png"),
        // Post's icon IS its cover: the indigo binding as the ground, the mid-century mailbox
        // painting set into the illustration window, and mitred brackets at the corners.
        new("Post", "FocusPhone/Post.Android/Resources", "appicon", false, Glyphs.PostPlate,
            Tokens.PostDeepHex, PlateImage: "Branding/post-cover.png"),
        // MAUI heads: resources live in the plain `Resources/` folder, not `Platforms/Android/Resources`.
        // These projects don't set <SingleProject>true</SingleProject>, so MAUI never repoints the Android
        // resource prefix at Platforms/Android/Resources — the active prefix stays the .NET-for-Android
        // default (`Resources`). Emitting here is what actually gets the mipmaps packaged (see the two heads'
        // csproj comments and CLAUDE.md); the launcher icon is then wired via [Application(Icon=...)].
        new("Canonizer", "Storyboarder/Storyboarder.Android/Resources", "appicon", true, Glyphs.PenNib),
        new("DailyLoader", "DailyLoader/DailyLoader.Android/Resources", "ic_launcher", true, Glyphs.FoldedPaper),
        new("Ledger", "Ledger/Ledger.Android/Resources", "appicon", true, Glyphs.Ledger),
    ];
}
