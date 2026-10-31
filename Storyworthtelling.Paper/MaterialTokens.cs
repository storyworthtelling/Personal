using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// The materials an app's furniture is drawn from: wood, bronze, and the two papers a label lies on.
///
/// <para>A registered token file (DESIGN.md §19). It exists because §19 says a MAUI head that has
/// adopted the shared kit has <b>no token file at all</b>, and Folio's desk and shelf are drawn in
/// ten colours that are neither a role nor an identity — a role says what something <i>means</i> and
/// an identity says <i>whose</i> it is, and a plank of wood is neither. Flyleaf reached the same
/// place first and solved it with a <c>Shelf</c> group inside its own tokens, which is available to
/// a native head and not to an adopted one; this is the shared answer, so that the next app to draw
/// a piece of furniture finds one rather than inventing a second wood.</para>
///
/// <para><b>Two of these combinations can never carry text, and that is the point of them.</b> Muted
/// ink on the desk measures 1.40:1 and cream on bronze 2.80:1, both far under §17's floor. Neither
/// is a defect to darken away: the design puts every label on a paper note precisely because of it,
/// which is what makes the contrast legal. <c>MaterialContrastTests</c> records both, so a later
/// screen that letters straight onto the wood fails rather than shipping something unreadable.</para>
/// </summary>
public static class MaterialTokens
{
    // The desk seen from above: a warm plank lit from the top left, its grain drawn rather than
    // rendered. The two ends of the gradient and the pencil the grain is drawn in.
    public const string DeskWoodLightHex = "#CE9457";
    public const string DeskWoodDeepHex = "#B0713A";
    public const string DeskGrainHex = "#7E5024";

    // The shelf: a ledge the books stand on, and the bronze bookends that bracket a series.
    public const string LedgeHex = "#B4885A";
    public const string BronzeHex = "#C08B52";
    public const string BronzeEdgeHex = "#8A5C2A";

    // The two papers. A note card is the cream a book's details are written on; a post-it is what a
    // passing state is stuck on. Both exist so that ink has somewhere legal to sit on the wood.
    public const string NoteCardHex = "#FFFEF8";
    public const string PostItHex = "#FBF3C9";
    public const string PostItEdgeHex = "#E4D79A";

    // The index card's two rule colours and their pitch (C-3.2.1, PC6). They lived in Index's own
    // token file; they are here so Index and Constitution draw ONE card: book p.11 builds
    // Constitution's meal cards from "Index's own" stock, and the mockup's `.scrap.index` is Index's
    // `.ix-hd` / `.ix-body` rules. A red rule under the head, pale blue body rules every 22 dp.
    // Decimal (not double) for every fraction and size below, as in Index's file: these are drawing
    // constants, and a whole-assembly scan for floating-point members elsewhere has nothing to flag.
    public const string CardHeadRuleHex = "#E4A7A2";
    public const string CardBodyRuleHex = "#D6E3EF";
    public const decimal CardRulePitchDp = 22m;

    // ----- The scrapbook (Constitution, D41-D48; book p.11-p.16). Values are the mockups' CSS. -----
    // DESIGN.md section 2 says paper is one cream everywhere; these are a recorded departure
    // (C-3.1.3 writes it up). Every alpha is an ALPHA over a ground, never a second hex.

    // The crinkled ground (`.dv-well`'s inline SVG): one grey-olive pencil, drawn as creases (1 px,
    // 7%) and short crinkle ticks (0.7 px, 4% to 9%). The painter (C-3.2.2) picks a tick's alpha
    // within the range from its seed.
    public const string CreaseInkHex = "#6B6759";
    public const decimal CreaseAlpha = 0.07m;
    public const decimal CreaseStrokeWidth = 1.0m;
    public const string TickInkHex = "#6B6759";
    public const decimal TickAlphaMin = 0.04m;
    public const decimal TickAlphaMax = 0.09m;
    public const decimal TickStrokeWidth = 0.7m;

    // The one tear line across the ground (`.perf`): 1.5 px dotted.
    public const string PerforationInkHex = "#6B6759";
    public const decimal PerforationAlpha = 0.22m;
    public const decimal PerforationWidth = 1.5m;

    // Scotch tape (`.tape`): translucent cream with a faint edge.
    public const string TapeHex = "#F4ECCE";
    public const decimal TapeAlpha = 0.62m;
    public const string TapeEdgeHex = "#8A8578";
    public const decimal TapeEdgeAlpha = 0.18m;

    // Magazine clipping (`.scrap.mag`): stock, its 1.5 px scissor-edge inset, a halftone dot screen
    // (dots 3 px apart at 7%) and the sliver of the next column.
    public const string MagazineStockHex = "#FDFCF9";
    public const string MagazineEdgeHex = "#CFC6B2";
    public const string HalftoneInkHex = "#33322B";
    public const decimal HalftoneAlpha = 0.07m;
    public const int HalftonePitchDp = 3;
    public const string MagazineColumnInkHex = "#6B6759";

    // Notepad leaf (`.scrap.note`): the card's cream, ruled every 24 px in the hairline.
    public const string NotepadLeafHex = NoteCardHex;
    public const string NotepadRuleHex = "#E3DCC9";
    public const int NotepadRulePitchDp = 24;

    // Receipt (`.scrap.receipt`) and the doctor's chart (`.scrap.grid`): white stock. The chart's
    // 12 px grid is the Reference role's mark at 10% (rgba(76,103,135,.10)), asserted equal to
    // PaperColorTokens.ReferenceMark by MaterialParityTests rather than trusted.
    public const string ReceiptHex = "#FFFFFF";
    public const string ChartPaperHex = "#FFFFFF";
    public const string ChartGridHex = "#4C6787";
    public const decimal ChartGridAlpha = 0.10m;
    public const int ChartGridPitchDp = 12;

    public static SKColor DeskWoodLight { get; } = SKColor.Parse(DeskWoodLightHex);
    public static SKColor DeskWoodDeep { get; } = SKColor.Parse(DeskWoodDeepHex);
    public static SKColor DeskGrain { get; } = SKColor.Parse(DeskGrainHex);
    public static SKColor Ledge { get; } = SKColor.Parse(LedgeHex);
    public static SKColor Bronze { get; } = SKColor.Parse(BronzeHex);
    public static SKColor BronzeEdge { get; } = SKColor.Parse(BronzeEdgeHex);
    public static SKColor NoteCard { get; } = SKColor.Parse(NoteCardHex);
    public static SKColor PostIt { get; } = SKColor.Parse(PostItHex);
    public static SKColor PostItEdge { get; } = SKColor.Parse(PostItEdgeHex);
}
