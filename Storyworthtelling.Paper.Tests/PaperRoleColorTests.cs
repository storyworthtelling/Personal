using SkiaSharp;
using Storyworthtelling.Paper;

namespace Storyworthtelling.Paper.Tests;

// The P4 lively role model (DESIGN.md §3). These are the build-failing guarantees the expansion promised:
//   • text is legible — every role's Text and Deep tiers, and ink over its wash, clear WCAG AA against the
//     WORST-CASE textured pixel (paper darkened by the §6 grain), not the flat token;
//   • the model is complete — every role resolves in every profile;
//   • the rM2 gray ramp stays a deliberate, separable, monotonic ramp of true greys.
// Mark/Fill tiers are strokes and fills paired with a shape cue (§2), so the binding legibility gate is the
// text tiers; that is what is asserted here.
public class PaperRoleColorTests
{
    private static readonly ColorRole[] Roles =
    [
        ColorRole.Action, ColorRole.Positive, ColorRole.Resolved, ColorRole.Reference,
        ColorRole.Caution, ColorRole.Problem, ColorRole.InProgress,
    ];

    // The darkest pixel text sits on: the paper cream with the §6 grain (ink at 6/255) composited over it.
    private static readonly SKColor WorstTexturedPixel = Composite(
        SKColor.Parse(PaperColorTokens.Ink), SKColor.Parse(PaperColorTokens.Paper), PaperColorTokens.Grain.Alpha / 255.0);

    private const double AA = 4.5;      // body text
    private const double AaLarge = 3.0; // large / UI

    [Fact]
    public void EveryRoleTextTierClearsAaOnTheWorstTexturedPixel()
    {
        foreach (var role in Roles)
        {
            var text = PaperPalette.ResolveRole(role, ColorTier.Text, UiColorProfile.App);
            var ratio = Contrast(text, WorstTexturedPixel);
            Assert.True(ratio >= AA, $"{role} Text tier is {ratio:0.00}:1 on the worst textured pixel (needs {AA}).");
        }
    }

    [Fact]
    public void EveryRoleDeepTierClearsAaOnTheWorstTexturedPixel()
    {
        foreach (var role in Roles)
        {
            var deep = PaperPalette.ResolveRole(role, ColorTier.Deep, UiColorProfile.App);
            Assert.True(Contrast(deep, WorstTexturedPixel) >= AA, $"{role} Deep tier fails AA.");
        }
    }

    [Fact]
    public void InkOverEveryRoleWashStaysLegible()
    {
        // The highlighter selects by laying a translucent wash over paper; ink text on top must still read.
        var ink = SKColor.Parse(PaperColorTokens.Ink);
        foreach (var role in Roles)
        {
            var wash = PaperPalette.ResolveRole(role, ColorTier.Wash, UiColorProfile.App);
            var bg = Composite(wash, WorstTexturedPixel, wash.Alpha / 255.0);
            Assert.True(Contrast(ink, bg) >= AA, $"Ink over the {role} highlighter wash fails AA.");
        }
    }

    [Fact]
    public void InkBlueVoiceClearsAa()
    {
        Assert.True(Contrast(SKColor.Parse(PaperColorTokens.InkBlue), WorstTexturedPixel) >= AA,
            "Ink-blue (the voice) fails AA on paper.");
    }

    [Theory]
    [InlineData(UiColorProfile.App)]
    [InlineData(UiColorProfile.DocumentColor)]
    [InlineData(UiColorProfile.DocumentGray)]
    public void EveryRoleResolvesInEveryTierAndProfile(UiColorProfile profile)
    {
        foreach (var role in Roles)
            foreach (ColorTier tier in Enum.GetValues<ColorTier>())
                _ = PaperPalette.ResolveRole(role, tier, profile); // must not throw
    }

    [Fact]
    public void AppMarkTiersAreAllDistinct()
    {
        var marks = Roles.Select(r => PaperPalette.ResolveRole(r, ColorTier.Mark, UiColorProfile.App)).ToList();
        Assert.Equal(Roles.Length, marks.Distinct().Count());
    }

    [Fact]
    public void DocumentGrayRampIsTrueGrayDistinctAndMonotonic()
    {
        // Problem darkest → Caution lightest, strictly increasing, so the seven roles stay separable on the rM2.
        var order = new[]
        {
            ColorRole.Problem, ColorRole.Action, ColorRole.InProgress, ColorRole.Reference,
            ColorRole.Resolved, ColorRole.Positive, ColorRole.Caution,
        };
        var values = order.Select(r =>
        {
            var c = PaperPalette.ResolveRole(r, ColorTier.Mark, UiColorProfile.DocumentGray);
            Assert.True(c.Red == c.Green && c.Green == c.Blue, $"{r} is not a true gray under the rM2 profile.");
            return (int)c.Red;
        }).ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
        for (var i = 1; i < values.Count; i++)
            Assert.True(values[i] > values[i - 1], $"Gray ramp not strictly increasing at {order[i]}.");
    }

    [Fact]
    public void DocumentColorProfileIsMutedAndDistinctFromApp()
    {
        // Documents render the muted per-role hue, not the lively App mark.
        foreach (var role in Roles)
        {
            Assert.NotEqual(
                PaperPalette.ResolveRole(role, ColorTier.Mark, UiColorProfile.App),
                PaperPalette.ResolveRole(role, ColorTier.Mark, UiColorProfile.DocumentColor));
        }
    }

    [Fact]
    public void GeneratedPaletteMatchesCommittedTokens()
    {
        // The committed App tokens are the OKLCH generator's output; regenerate and assert equality so no
        // one hand-edits them. Change the palette by changing PaletteSpec, then re-syncing the tokens.
        var app = new Dictionary<(ColorRole, ColorTier), string>
        {
            [(ColorRole.Action, ColorTier.Mark)] = PaperColorTokens.ActionMark,
            [(ColorRole.Action, ColorTier.Text)] = PaperColorTokens.ActionText,
            [(ColorRole.Action, ColorTier.Deep)] = PaperColorTokens.ActionDeep,
            [(ColorRole.Action, ColorTier.Fill)] = PaperColorTokens.ActionFill,
            [(ColorRole.Positive, ColorTier.Mark)] = PaperColorTokens.PositiveMark,
            [(ColorRole.Positive, ColorTier.Text)] = PaperColorTokens.PositiveText,
            [(ColorRole.Positive, ColorTier.Deep)] = PaperColorTokens.PositiveDeep,
            [(ColorRole.Positive, ColorTier.Fill)] = PaperColorTokens.PositiveFill,
            [(ColorRole.Resolved, ColorTier.Mark)] = PaperColorTokens.ResolvedMark,
            [(ColorRole.Resolved, ColorTier.Text)] = PaperColorTokens.ResolvedText,
            [(ColorRole.Resolved, ColorTier.Deep)] = PaperColorTokens.ResolvedDeep,
            [(ColorRole.Resolved, ColorTier.Fill)] = PaperColorTokens.ResolvedFill,
            [(ColorRole.Reference, ColorTier.Mark)] = PaperColorTokens.ReferenceMark,
            [(ColorRole.Reference, ColorTier.Text)] = PaperColorTokens.ReferenceText,
            [(ColorRole.Reference, ColorTier.Deep)] = PaperColorTokens.ReferenceDeep,
            [(ColorRole.Reference, ColorTier.Fill)] = PaperColorTokens.ReferenceFill,
            [(ColorRole.Caution, ColorTier.Mark)] = PaperColorTokens.CautionMark,
            [(ColorRole.Caution, ColorTier.Text)] = PaperColorTokens.CautionText,
            [(ColorRole.Caution, ColorTier.Deep)] = PaperColorTokens.CautionDeep,
            [(ColorRole.Caution, ColorTier.Fill)] = PaperColorTokens.CautionFill,
            [(ColorRole.Problem, ColorTier.Mark)] = PaperColorTokens.ProblemMark,
            [(ColorRole.Problem, ColorTier.Text)] = PaperColorTokens.ProblemText,
            [(ColorRole.Problem, ColorTier.Deep)] = PaperColorTokens.ProblemDeep,
            [(ColorRole.Problem, ColorTier.Fill)] = PaperColorTokens.ProblemFill,
            [(ColorRole.InProgress, ColorTier.Mark)] = PaperColorTokens.ProgressMark,
            [(ColorRole.InProgress, ColorTier.Text)] = PaperColorTokens.ProgressText,
            [(ColorRole.InProgress, ColorTier.Deep)] = PaperColorTokens.ProgressDeep,
            [(ColorRole.InProgress, ColorTier.Fill)] = PaperColorTokens.ProgressFill,
        };

        foreach (var (role, tier, hex) in PaletteSpec.Generate())
            Assert.Equal(hex, app[(role, tier)]);

        var doc = new Dictionary<ColorRole, string>
        {
            [ColorRole.Action] = PaperColorTokens.ActionDoc,
            [ColorRole.Positive] = PaperColorTokens.PositiveDoc,
            [ColorRole.Resolved] = PaperColorTokens.ResolvedDoc,
            [ColorRole.Reference] = PaperColorTokens.ReferenceDoc,
            [ColorRole.Caution] = PaperColorTokens.CautionDoc,
            [ColorRole.Problem] = PaperColorTokens.ProblemDoc,
            [ColorRole.InProgress] = PaperColorTokens.ProgressDoc,
        };
        var gray = new Dictionary<ColorRole, string>
        {
            [ColorRole.Action] = PaperColorTokens.ActionGray,
            [ColorRole.Positive] = PaperColorTokens.PositiveGray,
            [ColorRole.Resolved] = PaperColorTokens.ResolvedGray,
            [ColorRole.Reference] = PaperColorTokens.ReferenceGray,
            [ColorRole.Caution] = PaperColorTokens.CautionGray,
            [ColorRole.Problem] = PaperColorTokens.ProblemGray,
            [ColorRole.InProgress] = PaperColorTokens.ProgressGray,
        };
        foreach (var role in Roles)
        {
            Assert.Equal(PaletteSpec.DocHex(role), doc[role]);
            Assert.Equal(PaletteSpec.GrayHex(role), gray[role]);
        }
    }

    // ---- WCAG helpers ----
    private static double Contrast(SKColor a, SKColor b)
    {
        var (la, lb) = (RelativeLuminance(a), RelativeLuminance(b));
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(SKColor c)
    {
        double Ch(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.Red) + 0.7152 * Ch(c.Green) + 0.0722 * Ch(c.Blue);
    }

    // Composite `fg` at `alpha` over opaque `bg` (simple sRGB source-over — the on-screen result).
    private static SKColor Composite(SKColor fg, SKColor bg, double alpha)
    {
        byte Mix(byte f, byte b) => (byte)Math.Round(f * alpha + b * (1 - alpha));
        return new SKColor(Mix(fg.Red, bg.Red), Mix(fg.Green, bg.Green), Mix(fg.Blue, bg.Blue));
    }
}
