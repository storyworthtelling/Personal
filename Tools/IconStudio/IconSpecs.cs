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
/// background edge to edge. The window is what the round mask crops to, so the art reads as a
/// picture rather than as a picture inside a frame. Drawn before the glyph, which draws the
/// window's keyline over it.
/// </param>
/// <param name="PlateInset">How far in from each edge the window sits, as a fraction of the layer.</param>
/// <param name="CoverImage">
/// Where to write the shelf cover — the art Flyleaf paints into a book's illustration
/// window, repo-relative. It is the ground and the glyph and nothing else: the plate's
/// frame, corners and title belong to the shelf, which draws them around this.
///
/// It exists because the cover shipped as the bare ground wash with no keyboard on it,
/// which is what a hand-placed export of the background layer looks like. Generating it
/// from the same spec as the icon is what stops the two drifting apart again.
/// </param>
/// <param name="CoverDrawsGlyph">
/// Whether the shelf cover (<see cref="CoverImage"/>) runs the ordinary background + windowed
/// plate + <see cref="Glyph"/> construction, rather than shipping the bare, cropped painting
/// alone. Every other cover with a <see cref="PlateImage"/> ships the art alone — the shelf
/// already draws its own cloth, rule and title band around it, and baking a second keyline in
/// would double one up at a different scale (see <c>AdaptiveIconRenderer.RenderCover</c>).
/// Index was briefly the one exception (D119: its corner flourishes are <see cref="Glyph"/>'s
/// own top layer), and so doubled the shelf's own brackets; the shelf owns the rule, the title
/// band and the brackets, so Index's cover is window only too and no spec sets this now. The
/// flourishes remain on the launcher icon, where the round mask crops them.
/// </param>
/// <param name="Crop">
/// The part of <see cref="PlateImage"/> to draw, in source pixels. Must be square and inside the
/// image. Null (every spec so far) keeps the largest centred square.
/// </param>
public sealed record IconSpec(
    string App,
    string ResourceRoot,
    string Name,
    bool WritesColorsXml,
    Action<SKCanvas, float> Glyph,
    string BackgroundHex = Tokens.PaperHex,
    string? GroundImage = null,
    string? PlateImage = null,
    float PlateInset = Glyphs.FullBleedInset,
    string? CoverImage = null,
    bool CoverDrawsGlyph = false,
    SourceCrop? Crop = null);

/// <summary>A rectangle in the source painting's own pixels. Integers, so a crop is exact and reproducible.</summary>
public sealed record SourceCrop(int X, int Y, int Width, int Height);

public static class IconSpecs
{
    public static readonly IReadOnlyList<IconSpec> All =
    [
        new("FocusPhone.Launcher", "FocusPhone/Flyleaf.Android/Resources", "appicon", false, Glyphs.Bookplate, Tokens.FlyleafWarmHex),
        // Plate's icon IS its cover, like Post's: the light mauve binding as the ground and the
        // chosen lens study filling the window, keylined in ink. It shipped
        // its whole rebrand still wearing Glyphs.Lens on plain paper — the pre-rebrand Camera
        // icon, in the retired terracotta — because the art was chosen at gate 3 and never
        // committed. CoverImage is what puts the same picture on the Flyleaf shelf.
        new("Plate", "FocusPhone/Plate.Android/Resources", "appicon", false, Glyphs.PlatePlate,
            Tokens.PlateGroundHex, PlateImage: "Branding/plate-cover.jpg",
            CoverImage: "FocusPhone/Flyleaf.Android/Resources/drawable/cover_plate.png"),
        new("Score", "FocusPhone/Score.Android/Resources", "appicon", false, Glyphs.Keyboard,
            Tokens.ScoreGroundHex, GroundImage: "Branding/score-ground.png",
            CoverImage: "FocusPhone/Flyleaf.Android/Resources/drawable/cover_score.png"),
        // Post's icon IS its cover: the indigo binding as the ground and the mid-century mailbox
        // painting filling the window, keylined in cream.
        new("Post", "FocusPhone/Post.Android/Resources", "appicon", false, Glyphs.PostPlate,
            Tokens.PostDeepHex, PlateImage: "Branding/post-cover.png"),
        // MAUI heads: resources live in the plain `Resources/` folder, not `Platforms/Android/Resources`.
        // These projects don't set <SingleProject>true</SingleProject>, so MAUI never repoints the Android
        // resource prefix at Platforms/Android/Resources — the active prefix stays the .NET-for-Android
        // default (`Resources`). Emitting here is what actually gets the mipmaps packaged (see the two heads'
        // csproj comments and CLAUDE.md); the launcher icon is then wired via [Application(Icon=...)].
        // Folio's icon IS its cover, like Post's and Plate's: the deep red binding as the ground
        // and the linocut filling the window, keylined in cream. It
        // shipped its whole rebrand still wearing Glyphs.PenNib on plain paper — Canonizer's red
        // nib, the pre-rebrand icon — because the rename moved this line's name and output path and
        // left its glyph alone. CoverImage is what puts the same picture on the Flyleaf shelf,
        // where Folio was the one sibling app with no book.
        new("Folio", "Storyboarder/Folio.Android/Resources", "appicon", true, Glyphs.FolioPlate,
            Tokens.FolioClothHex, PlateImage: "Branding/folio-cover.png",
            CoverImage: "FocusPhone/Flyleaf.Android/Resources/drawable/cover_folio.png"),
        // Foreword's icon IS its cover: the sunrise ground and the engraved doorstep, full bleed.
        // It was the first to drop its corner brackets — the window is too wide for them to
        // survive a round mask, and the engraving needed the room — and the keyline-only cover is
        // now the shape every cover icon takes, by the owner's decision. WritesColorsXml stays true:
        // this head's values/colors.xml is the one the MAUI resource glob actually packages.
        new("Foreword", "Foreword/Foreword.Android/Resources", "ic_launcher", true, Glyphs.ForewordPlate,
            Tokens.ForewordSunriseHex, PlateImage: "Branding/foreword-cover.png"),
        // Index's icon IS its cover, like Post's/Folio's/Plate's/Foreword's: the deep
        // bookplate-green binding as the ground and the coin-staircase painting filling the
        // window, keylined in cream — with one departure (D119): a quarter-curve flourish
        // reflected into each corner, entirely inside FullBleedInset's own outer margin, so the
        // round-masked launcher crops it away and only the legacy square
        // shows it. Its shelf COVER is the bare painting like every other cover (the shelf draws
        // its own rule, band and brackets, and the flourish would double them). Replaces the retired
        // Ledger spec (Index is the same package's rebrand, not an additional app) — Index
        // commits its generated icons like every other head, since Ledger was the one
        // exception to that.
        new("Index", "Ledger/Ledger.Android/Resources", "appicon", true, Glyphs.IndexPlate,
            Tokens.IndexClothHex, PlateImage: "Branding/index-cover.png",
            CoverImage: "FocusPhone/Flyleaf.Android/Resources/drawable/cover_index.png"),
        // Pulp's icon IS its cover (D200, D266, D267): the den — the cliffhanger projected on a
        // screen, the popcorn on the table in front — filling the mask with no keyline and no amber
        // border. Set at the shared full-bleed inset rather than at zero, because the painting is
        // portrait and the mask keeps only the middle two-thirds: at zero the popcorn falls out of
        // the circle, and D266 is that the crop holds the screen and the popcorn together. The
        // amber cloth is the ground a launcher shows during its parallax, and the shelf binding.
        new("Pulp", "Pulp/Pulp.Android/Resources", "appicon", true, Glyphs.PulpPlate,
            Tokens.PulpClothHex, PlateImage: "Branding/pulp-cover.jpg",
            CoverImage: "FocusPhone/Flyleaf.Android/Resources/drawable/cover_pulp.png"),
        // Constitution's icon IS its cover, like Pulp's: the approved painting full bleed on the
        // cerulean cloth with no keyline (book page 3, icon B; D12). The painting is portrait
        // (864x1184), so the crop is a closer square on the slope, rows 190-974 centred, which is
        // what keeps the two runners large enough to read as two figures at 48 px.
        new("Constitution", "Constitution/Constitution.Android/Resources", "appicon", false, Glyphs.ConstitutionPlate,
            Tokens.ConstitutionClothHex, PlateImage: "Branding/constitution-cover.jpg",
            CoverImage: "FocusPhone/Flyleaf.Android/Resources/drawable/cover_constitution.png",
            Crop: new SourceCrop(40, 190, 784, 784)),
    ];
}
