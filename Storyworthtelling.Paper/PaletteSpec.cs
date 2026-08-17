namespace Storyworthtelling.Paper;

/// <summary>
/// The single OKLCH source spec the P4 palette is generated from (DESIGN.md §3/§19). Each role sits at a
/// fixed hue and chroma; the five tiers share a perceptual-lightness ladder so the whole family is coherent
/// and the text/deep tiers land at AA by construction. The muted document hue is the same hue at reduced
/// chroma; the rM2 gray ramp is a set of separable neutral lightnesses. <see cref="Generate"/> emits the
/// hex the committed <see cref="PaperColorTokens"/> mirror; a parity test regenerates and asserts equality,
/// so no one hand-edits the generated values.
/// </summary>
public static class PaletteSpec
{
    // Role position on the OKLCH wheel: (hue°, chroma). Resolved is near-neutral warm gray.
    private static readonly IReadOnlyDictionary<ColorRole, (double Hue, double Chroma)> RoleAxis =
        new Dictionary<ColorRole, (double, double)>
        {
            [ColorRole.Action] = (152, 0.086),      // pine
            [ColorRole.Positive] = (129, 0.104),    // leaf
            [ColorRole.Resolved] = (79, 0.010),     // warm gray
            [ColorRole.Reference] = (253, 0.058),   // slate-blue
            [ColorRole.Caution] = (85, 0.099),      // ochre
            [ColorRole.Problem] = (17, 0.115),      // rose
            [ColorRole.InProgress] = (207, 0.076),  // teal
        };

    // Perceptual-lightness ladder shared by every role, with a per-tier chroma scale.
    private static readonly IReadOnlyDictionary<ColorTier, (double L, double CScale)> TierLadder =
        new Dictionary<ColorTier, (double, double)>
        {
            [ColorTier.Fill] = (0.800, 0.62),
            [ColorTier.Mark] = (0.505, 1.05),
            [ColorTier.Text] = (0.435, 0.95),
            [ColorTier.Deep] = (0.335, 0.78),
            // Wash is the Mark hue carried at PaperColorTokens.WashAlpha; it has no committed hex.
            [ColorTier.Wash] = (0.505, 1.05),
        };

    // Muted document profile: the role hue at reduced chroma and a fixed lightness.
    private const double DocLightness = 0.520;
    private const double DocChromaScale = 0.62;

    // rM2 gray ramp — separable neutral lightnesses (dark→light), ordered so the seven roles stay distinct.
    private static readonly IReadOnlyDictionary<ColorRole, double> GrayL = new Dictionary<ColorRole, double>
    {
        [ColorRole.Problem] = 0.360,
        [ColorRole.Action] = 0.440,
        [ColorRole.InProgress] = 0.500,
        [ColorRole.Reference] = 0.545,
        [ColorRole.Resolved] = 0.605,
        [ColorRole.Positive] = 0.670,
        [ColorRole.Caution] = 0.720,
    };

    /// <summary>The lively App-profile hex for a role's tier (wash has no hex — use the mark at wash alpha).</summary>
    public static string AppHex(ColorRole role, ColorTier tier)
    {
        var (hue, chroma) = RoleAxis[role];
        var (l, cScale) = TierLadder[tier];
        return Oklch.ToHex(l, chroma * cScale, hue);
    }

    /// <summary>The muted document-profile hex for a role.</summary>
    public static string DocHex(ColorRole role)
    {
        var (hue, chroma) = RoleAxis[role];
        return Oklch.ToHex(DocLightness, chroma * DocChromaScale, hue);
    }

    /// <summary>The rM2 gray-ramp hex for a role (a true neutral).</summary>
    public static string GrayHex(ColorRole role) => Oklch.ToHex(GrayL[role], 0, 0);

    /// <summary>Every generated value, for the generated↔committed parity test.</summary>
    public static IEnumerable<(ColorRole Role, ColorTier Tier, string Hex)> Generate()
    {
        foreach (var role in RoleAxis.Keys)
        {
            foreach (var tier in new[] { ColorTier.Fill, ColorTier.Mark, ColorTier.Text, ColorTier.Deep })
                yield return (role, tier, AppHex(role, tier));
        }
    }
}
