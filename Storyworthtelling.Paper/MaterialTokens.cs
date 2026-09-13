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
