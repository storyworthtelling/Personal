namespace Storyworthtelling.Materials;

/// <summary>The paper grain's tokens (DESIGN.md §2): ink flecks on every paper ground and in every document.</summary>
public static class Grain
{
    public const int TileSize = 256;

    /// <summary>A fleck's ink, of 255: about 12.5%.</summary>
    public const int Alpha = 32;

    /// <summary>About one pixel in this many carries a fleck.</summary>
    public const int Density = 6;

    public const uint Seed = 1913;
}

/// <summary>
/// How strong each material is: the "Strength" column of the materials table approved in W8.8 (DESIGN.md §2).
/// Fractions are of the full colour.
/// </summary>
public static class MaterialStrength
{
    /// <summary>Colored pencil: a pixel that loses colour keeps at least this much of it.</summary>
    public const double PencilKeepsAtLeast = 0.3;

    /// <summary>Colored pencil: and at most this much.</summary>
    public const double PencilKeepsAtMost = 0.7;

    /// <summary>Colored pencil: the share of pixels that drop out entirely.</summary>
    public const double PencilDropsOut = 0.1;

    /// <summary>Colored pencil: the share of the rest that lose some colour.</summary>
    public const double PencilFlakes = 0.5;

    /// <summary>Colored pencil: one tooth texel, in dp, so the grain is the same size on every screen (a 2dp stroke is four texels wide).</summary>
    public const float PencilTexelDp = 0.5f;

    /// <summary>Marker: neighbouring streaks differ by up to this much.</summary>
    public const double MarkerStreak = 0.2;

    /// <summary>Marker: the edges are up to this much darker, where the felt pressed.</summary>
    public const double MarkerEdge = 0.12;

    /// <summary>Marker: the last tenth of a stroke thins by up to this much.</summary>
    public const double MarkerEndThins = 0.25;

    /// <summary>Highlighter: the wash tier's alpha, of 255 (30%).</summary>
    public const int WashAlpha = 77;

    /// <summary>Highlighter: the faintest streak.</summary>
    public const double HighlighterLeast = 0.22;

    /// <summary>Highlighter: the strongest streak.</summary>
    public const double HighlighterMost = 0.38;

    /// <summary>Highlighter: where the nib started and lifted, the colour pools to this.</summary>
    public const double HighlighterPool = 0.45;

    /// <summary>Pressed: the surface's grain or tooth rises by this much while held.</summary>
    public const double PressLift = 0.5;

    /// <summary>Marker and highlighter: one streak row, in dp, so a button's fibres are the same size on every screen.</summary>
    public const float StreakRowDp = 0.75f;
}
