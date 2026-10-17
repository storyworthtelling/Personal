using System.Text.RegularExpressions;
using Codex.Domain.Design;
using Storyworthtelling.Paper;
using Xunit;

namespace Codex.Tests.Design;

public class DesignTokensTests
{
    private static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>
    {
        ["Paper"] = DesignTokens.Paper,
        ["PaperBright"] = DesignTokens.PaperBright,
        ["PaperDeep"] = DesignTokens.PaperDeep,
        ["PaperShade"] = DesignTokens.PaperShade,
        ["Hairline"] = DesignTokens.Hairline,
        ["Ink"] = DesignTokens.Ink,
        ["InkSoft"] = DesignTokens.InkSoft,
        ["InkMuted"] = DesignTokens.InkMuted,
        ["Accent"] = DesignTokens.Accent,
        ["AccentDeep"] = DesignTokens.AccentDeep,
    };

    [Fact]
    public void AllColorsAreUppercaseSixDigitHex()
    {
        foreach (var (name, value) in Colors)
        {
            Assert.True(Regex.IsMatch(value, "^#[0-9A-F]{6}$"), $"{name} = {value} is not #RRGGBB uppercase hex");
        }
    }

    [Fact]
    public void AllColorsAreDistinct()
    {
        Assert.Equal(Colors.Count, Colors.Values.Distinct().Count());
    }

    [Theory]
    [InlineData("Ink", "Paper", 7.0)]          // AAA body text
    [InlineData("InkSoft", "Paper", 4.5)]      // AA body text
    [InlineData("InkMuted", "Paper", 3.0)]     // large/secondary text
    [InlineData("Accent", "Paper", 3.0)]       // UI components, large text
    [InlineData("AccentDeep", "Paper", 4.5)]   // accent text at any size
    [InlineData("PaperBright", "Accent", 4.5)] // primary button text
    [InlineData("Ink", "PaperDeep", 7.0)]      // warning card body
    [InlineData("Ink", "PaperShade", 7.0)]     // pressed surfaces
    public void ContrastMeetsDesignBibleThreshold(string fg, string bg, double minimum)
    {
        var ratio = DesignTestHelpers.ContrastRatio(Colors[fg], Colors[bg]);
        Assert.True(ratio >= minimum, $"{fg} on {bg} is {ratio:F2}:1, below {minimum}:1");
    }

    // Score's identity ladder, sampled from its cover per §3 and laddered into the
    // role model's five tiers. Pinned here because the hue is a decision, not a
    // derivation: it was nudged off the as-painted 174 degrees to clear the
    // in-progress status teal, and a later resample must not quietly undo that.
    [Fact]
    public void ScoreIdentityIsTheApprovedLadder()
    {
        Assert.Equal("#AFC0BC", DesignTokens.Identity.Score.Fill);
        Assert.Equal("#93ABA5", DesignTokens.Identity.Score.Ground);
        Assert.Equal("#436A61", DesignTokens.Identity.Score.Mark);
        Assert.Equal("#35544D", DesignTokens.Identity.Score.Text);
        Assert.Equal("#263B36", DesignTokens.Identity.Score.Deep);
    }

    // The two tiers Score sets text and marks in have to clear §3's contrast
    // binding on their own, the same as any semantic role. Fill and Ground are
    // wash/fill tiers and carry no text, so they are not asserted.
    [Theory]
    [InlineData("#436A61", 3.0)]  // marks and UI, incl. the lit binder tab
    [InlineData("#35544D", 4.5)]  // identity-colored text at any size
    [InlineData("#263B36", 7.0)]  // the deep tier, body-text safe
    public void ScoreIdentityTextTiersClearContrastOnPaper(string tier, double minimum)
    {
        var ratio = DesignTestHelpers.ContrastRatio(tier, DesignTokens.Paper);
        Assert.True(ratio >= minimum, $"{tier} on Paper is {ratio:F2}:1, below {minimum}:1");
    }

    // Post's identity ladder, sampled at 285 degrees from the cover's BINDING rather
    // than its painting — a deliberate departure from §3's usual route, recorded
    // there. The mailbox art samples warm, around 65 degrees, which is 14 from the
    // resolved gray and 24 from Flyleaf: an identity that would read as a status and
    // look like the launcher on the shelf. §12 already allows a binding to contrast
    // the art it wraps, so the binding is what was sampled. Lightnesses follow
    // Score's five-tier ladder exactly; chroma is roughly doubled.
    [Fact]
    public void PostIdentityIsTheApprovedLadder()
    {
        Assert.Equal("#B9B9D4", DesignTokens.Identity.Post.Fill);
        Assert.Equal("#A1A1C7", DesignTokens.Identity.Post.Ground);
        Assert.Equal("#5C5894", DesignTokens.Identity.Post.Mark);
        Assert.Equal("#494676", DesignTokens.Identity.Post.Text);
        Assert.Equal("#333252", DesignTokens.Identity.Post.Deep);
    }

    // The tiers Post sets text and marks in clear §3's contrast binding on their own,
    // the same as any semantic role. Fill and Ground are wash tiers and carry no text.
    [Theory]
    [InlineData("#5C5894", 3.0)]  // marks and UI, incl. the lit binder tab
    [InlineData("#494676", 4.5)]  // identity-colored text at any size
    [InlineData("#333252", 7.0)]  // the deep tier, body-text safe
    public void PostIdentityTextTiersClearContrastOnPaper(string tier, double minimum)
    {
        var ratio = DesignTestHelpers.ContrastRatio(tier, DesignTokens.Paper);
        Assert.True(ratio >= minimum, $"{tier} on Paper is {ratio:F2}:1, below {minimum}:1");
    }

    // Plate's ladder, against the same floors Post's is held to. The ink-on-cloth row is
    // the one Post does not need: Plate inverts the framing to ink on a LIGHT binding
    // (§12), so Ink has to clear AA on the ground tier rather than PaperBright on the deep.
    [Theory]
    [InlineData("#7A4F7B", 3.0)]  // marks and UI, incl. the lit binder tab
    [InlineData("#613F62", 4.5)]  // identity-colored text at any size
    [InlineData("#442D44", 7.0)]  // the deep tier, body-text safe
    public void PlateIdentityTextTiersClearContrastOnPaper(string tier, double minimum)
    {
        var ratio = DesignTestHelpers.ContrastRatio(tier, DesignTokens.Paper);
        Assert.True(ratio >= minimum, $"{tier} on Paper is {ratio:F2}:1, below {minimum}:1");
    }

    [Fact]
    public void PlateInkFramingClearsContrastOnItsLightBinding()
    {
        var ratio = DesignTestHelpers.ContrastRatio(DesignTokens.Ink, DesignTokens.Identity.Plate.Ground);
        Assert.True(ratio >= 4.5,
            $"Ink on Plate's binding cloth is {ratio:F2}:1, below AA — the inverted framing "
            + "(rule, brackets and lettering) would not be legible on the cover.");
    }

    // Pulp's identity ladder: amber, OKLCH 70 degrees (D275, superseding D202's marigold
    // 63), on the suite's shared lightness ladder. Pinned as literals because the hue is a
    // decision, not a sample: the marigold sat 16.3 degrees from Foreword's sunrise (47),
    // and no yellow-orange clears both Foreword and the caution ochre, so the owner chose
    // 70 — 22.4 clear of Foreword, and 14.3 from Caution as a RECORDED departure
    // (RecordedRoleDepartures below). An amber cannot carry text on paper at its own
    // brightness, so the text tiers are browns and the bright amber lives on the cloth.
    [Fact]
    public void PulpIdentityIsTheApprovedLadder()
    {
        Assert.Equal("#CCB8A2", DesignTokens.Identity.Pulp.Fill);
        Assert.Equal("#BCA080", DesignTokens.Identity.Pulp.Ground);
        Assert.Equal("#835618", DesignTokens.Identity.Pulp.Mark);
        Assert.Equal("#684414", DesignTokens.Identity.Pulp.Text);
        Assert.Equal("#493113", DesignTokens.Identity.Pulp.Deep);
        Assert.Equal("#F7AC4D", DesignTokens.Identity.Pulp.Cloth);
    }

    [Theory]
    [InlineData("#835618", 3.0)]  // marks and UI, incl. the lit binder tab
    [InlineData("#684414", 4.5)]  // identity-colored text at any size
    [InlineData("#493113", 7.0)]  // the deep tier, body-text safe
    public void PulpIdentityTextTiersClearContrastOnPaper(string tier, double minimum)
    {
        var ratio = DesignTestHelpers.ContrastRatio(tier, DesignTokens.Paper);
        Assert.True(ratio >= minimum, $"{tier} on Paper is {ratio:F2}:1, below {minimum}:1");
    }

    // Pulp is bound light, like Score and Plate: the rule, flourishes and title on the
    // amber cloth are ink, so ink has to clear AA on the cloth itself (6.72:1).
    [Fact]
    public void PulpInkFramingClearsContrastOnItsLightBinding()
    {
        var ratio = DesignTestHelpers.ContrastRatio(DesignTokens.Ink, DesignTokens.Identity.Pulp.Cloth);
        Assert.True(ratio >= 4.5,
            $"Ink on Pulp's amber cloth is {ratio:F2}:1, below AA — the inverted framing "
            + "(rule, flourishes and title) would not be legible on the cover.");
    }

    // IconStudio paints the icon and the cover from its own copy of the ladder; the Domain
    // ladder is what the Flyleaf shelf and every screen read. Two copies of a colour need a
    // check, and the suite's existing one (FrozenInvariantsTests) enumerates apps by hand,
    // so Pulp is held here, next to the ladder it guards. Read as source: Tools/ is a
    // separate build.
    [Theory]
    [InlineData("PulpFillHex", "Fill")]
    [InlineData("PulpGroundHex", "Ground")]
    [InlineData("PulpMarkHex", "Mark")]
    [InlineData("PulpTextHex", "Text")]
    [InlineData("PulpDeepHex", "Deep")]
    [InlineData("PulpClothHex", "Cloth")]
    public void PulpIconStudioHexesMatchTheDomainLadder(string hexName, string tier)
    {
        var tokens = File.ReadAllText(Path.Combine(
            DesignTestHelpers.FocusPhoneRoot(), "..", "Tools", "IconStudio", "Tokens.cs"));

        var declared = Regex.Match(tokens, $@"{hexName}\s*=\s*""(#[0-9A-Fa-f]{{6}})""");
        Assert.True(declared.Success,
            $"IconStudio/Tokens.cs declares no {hexName}, so Pulp's icon and cover cannot stand on "
            + "its identity and silently fall back to plain paper.");

        var domain = tier switch
        {
            "Fill" => DesignTokens.Identity.Pulp.Fill,
            "Ground" => DesignTokens.Identity.Pulp.Ground,
            "Mark" => DesignTokens.Identity.Pulp.Mark,
            "Text" => DesignTokens.Identity.Pulp.Text,
            "Deep" => DesignTokens.Identity.Pulp.Deep,
            "Cloth" => DesignTokens.Identity.Pulp.Cloth,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), $"Pulp.{tier} is not a mapped ladder tier."),
        };
        Assert.Equal(domain, declared.Groups[1].Value, ignoreCase: true);
    }

    // Constitution's identity ladder: cerulean, OKLCH 230 degrees, taken from the binding cloth
    // (D10). Pinned as literals because the hue is a decision: the centre of one of the two
    // windows left that clear every role and every sibling by 20 degrees.
    [Fact]
    public void ConstitutionIdentityIsTheApprovedLadder()
    {
        Assert.Equal("#A4C1D0", DesignTokens.Identity.Constitution.Fill);
        Assert.Equal("#82ACC1", DesignTokens.Identity.Constitution.Ground);
        Assert.Equal("#1B6A89", DesignTokens.Identity.Constitution.Mark);
        Assert.Equal("#15546E", DesignTokens.Identity.Constitution.Text);
        Assert.Equal("#143B4C", DesignTokens.Identity.Constitution.Deep);
        Assert.Equal("#82ACC1", DesignTokens.Identity.Constitution.Cloth);
    }

    [Theory]
    [InlineData("#1B6A89", 3.0)]  // marks and UI, incl. the lit binder tab
    [InlineData("#15546E", 4.5)]  // identity-colored text at any size
    [InlineData("#143B4C", 7.0)]  // the deep tier, body-text safe
    public void ConstitutionIdentityTextTiersClearContrastOnPaper(string tier, double minimum)
    {
        var ratio = DesignTestHelpers.ContrastRatio(tier, DesignTokens.Paper);
        Assert.True(ratio >= minimum, $"{tier} on Paper is {ratio:F2}:1, below {minimum}:1");
    }

    // Light binding, like Score, Plate and Pulp: rule, flourishes and title are ink on the cloth.
    [Fact]
    public void ConstitutionInkFramingClearsContrastOnItsLightBinding()
    {
        var ratio = DesignTestHelpers.ContrastRatio(DesignTokens.Ink, DesignTokens.Identity.Constitution.Cloth);
        Assert.True(ratio >= 4.5, $"Ink on Constitution's cerulean cloth is {ratio:F2}:1, below AA.");
    }

    // Foreword's and Folio's shelf bindings (D277) are copies of values registered elsewhere —
    // Foreword's sunrise in the printed-page identity tokens, Folio's cloth in IconStudio — and a
    // copy is only as good as the guard that holds it to its source.
    [Theory]
    [InlineData("Storyworthtelling.Paper/AppIdentityTokens.cs", "ForewordFillHex", "Foreword.Fill")]
    [InlineData("Storyworthtelling.Paper/AppIdentityTokens.cs", "ForewordGroundHex", "Foreword.Ground")]
    [InlineData("Storyworthtelling.Paper/AppIdentityTokens.cs", "ForewordSunriseHex", "Foreword.Mark")]
    [InlineData("Tools/IconStudio/Tokens.cs", "FolioClothHex", "Folio.Cloth")]
    public void ShelfBindingCopiesMatchTheirSource(string sourceFile, string hexName, string domainToken)
    {
        var source = File.ReadAllText(Path.Combine(DesignTestHelpers.FocusPhoneRoot(), "..", sourceFile));
        var declared = Regex.Match(source, $@"{hexName}\s*=\s*""(#[0-9A-Fa-f]{{6}})""");
        Assert.True(declared.Success, $"{sourceFile} declares no {hexName}; the shelf's copy has nothing to agree with.");

        var domain = domainToken switch
        {
            "Foreword.Fill" => DesignTokens.Identity.Foreword.Fill,
            "Foreword.Ground" => DesignTokens.Identity.Foreword.Ground,
            "Foreword.Mark" => DesignTokens.Identity.Foreword.Mark,
            "Folio.Cloth" => DesignTokens.Identity.Folio.Cloth,
            _ => throw new ArgumentOutOfRangeException(nameof(domainToken), $"{domainToken} is not mapped."),
        };
        Assert.Equal(domain, declared.Groups[1].Value, ignoreCase: true);
    }

    // §3's actual rule: an identity hue is sampled, then CHECKED against the semantic
    // roles it will sit near, and moved if it crowds one — so a tab never reads as a
    // status. That is a claim about degrees on the OKLCH wheel, and it was previously
    // "tested" by asserting two hex strings differ, which any two colors satisfy.
    //
    // 20 degrees is the floor every registered ladder clears but one: Flyleaf's mark at
    // 24 from the problem rose is the tightest that clears it. Resolved is excluded: at
    // 0.010 chroma it is a warm gray, and an angle measured off almost no chroma is not
    // a hue anyone can see.
    [Theory]
    [InlineData("Flyleaf", "#C9803A")] [InlineData("Flyleaf", "#B4552F")] [InlineData("Flyleaf", "#8A4A22")]
    [InlineData("Score", "#93ABA5")] [InlineData("Score", "#436A61")]
    [InlineData("Post", "#A1A1C7")] [InlineData("Post", "#5C5894")]
    [InlineData("Plate", "#C8B5C8")] [InlineData("Plate", "#B69BB6")] [InlineData("Plate", "#7A4F7B")]
    [InlineData("Plate", "#613F62")] [InlineData("Plate", "#442D44")]
    [InlineData("Pulp", "#CCB8A2")] [InlineData("Pulp", "#BCA080")] [InlineData("Pulp", "#835618")]
    [InlineData("Pulp", "#684414")] [InlineData("Pulp", "#493113")]
    [InlineData("Constitution", "#A4C1D0")] [InlineData("Constitution", "#82ACC1")] [InlineData("Constitution", "#1B6A89")]
    [InlineData("Constitution", "#15546E")] [InlineData("Constitution", "#143B4C")]
    public void IdentityTiersStayClearOfEverySemanticRole(string app, string identityTier)
    {
        var (_, _, hue) = Oklch.FromHex(identityTier);

        foreach (var (role, (roleHue, roleChroma)) in PaletteSpec.RoleWheel)
        {
            if (roleChroma < NearNeutralRoleChroma || RecordedRoleDepartures.Contains((app, role)))
            {
                continue;
            }
            var separation = Oklch.HueSeparation(hue, roleHue);
            Assert.True(separation >= MinimumRoleSeparation,
                $"{app}'s {identityTier} sits {separation:F1} degrees from the {role} role " +
                $"(minimum {MinimumRoleSeparation}) — an identity that close reads as a status.");
        }
    }

    private const double MinimumRoleSeparation = 20.0;
    private const double NearNeutralRoleChroma = 0.02;

    // The recorded departures from §3's 20-degree floor: an (app, role) pair the owner
    // accepted knowingly, each made inert by a rule rather than by distance. Named one by
    // one so a new departure is a decision someone writes down, never a widened floor.
    //
    // Pulp/Caution (D275): amber 70 sits 14.3 degrees from the caution ochre (85). No
    // yellow-orange clears both Foreword's sunrise (needs >= 67) and Caution (needs <= 65),
    // and the owner chose amber over leaving Pulp beside Foreword. It is inert because
    // Pulp draws no Caution on its own pages (Pulp.Tests NoCautionOnPulpPagesTests).
    //
    // Index's 0 degrees against Positive is NOT here: Index's ladder is not in this guard
    // (only its cloth lives in Domain), so it has nothing to be excepted from.
    private static readonly HashSet<(string App, ColorRole Role)> RecordedRoleDepartures =
    [
        ("Pulp", ColorRole.Caution),
    ];

    // The recorded departure is still where it was recorded. If Pulp drifts closer to
    // Caution the departure has quietly got worse; if it drifts past the floor the
    // exception is no longer needed and should be deleted rather than left standing.
    [Theory]
    [InlineData("#CCB8A2")] [InlineData("#BCA080")] [InlineData("#835618")]
    [InlineData("#684414")] [InlineData("#493113")]
    public void PulpCautionDepartureStaysWhereItWasRecorded(string pulpTier)
    {
        Assert.Contains(("Pulp", ColorRole.Caution), RecordedRoleDepartures);

        var separation = Oklch.HueSeparation(
            Oklch.FromHex(pulpTier).HueDegrees, PaletteSpec.RoleWheel[ColorRole.Caution].Hue);

        Assert.True(separation >= 14.0,
            $"Pulp's {pulpTier} is now {separation:F1} degrees from Caution, closer than the 14.3 "
            + "recorded in D275 (14.5 to 15.3 per tier as this guard measures them) — the departure has got worse, which is a new decision, not a drift.");
        Assert.True(separation < MinimumRoleSeparation,
            $"Pulp's {pulpTier} is now {separation:F1} degrees from Caution, clear of the "
            + $"{MinimumRoleSeparation} floor — the recorded departure is no longer needed; delete it.");
    }

    // §9: an inactive binder tab is cut from its area's own coloured cloth — the
    // fill tier — and its glyph and label are plain Ink. That only works if Ink is
    // legible on every cloth an area might take, which is a property of the ladder
    // rather than of any one app, so it is asserted across the whole fill row. A
    // label is small text, so AA body applies.
    [Theory]
    [InlineData("#A5C8AD")] [InlineData("#B1C69A")] [InlineData("#C0BDB9")] [InlineData("#AEC0D5")]
    [InlineData("#D0BB91")] [InlineData("#E8ACAD")] [InlineData("#9BC7CD")]   // the seven roles
    [InlineData("#AFC0BC")] [InlineData("#B9B9D4")]                            // Score, Post
    [InlineData("#CCB8A2")]                                                    // Pulp
    [InlineData("#A4C1D0")]                                                    // Constitution
    public void InkIsLegibleOnEveryBinderTabCloth(string cloth)
    {
        var ratio = DesignTestHelpers.ContrastRatio(DesignTokens.Ink, cloth);
        Assert.True(ratio >= 4.5, $"Ink on the {cloth} tab cloth is {ratio:F2}:1, below 4.5:1");
    }

    // The reading ladder. Countdown and CoverTitle sit outside it — they are
    // single-purpose sizes, not steps you would set prose at — so they are not
    // part of this ordering.
    [Fact]
    public void TypeScaleDescendsFromDisplayToLabel()
    {
        var scale = new[]
        {
            DesignTokens.TypeSp.Display,
            DesignTokens.TypeSp.Title,
            DesignTokens.TypeSp.Heading,
            DesignTokens.TypeSp.Body,
            DesignTokens.TypeSp.Secondary,
            DesignTokens.TypeSp.Label,
        };
        for (var i = 1; i < scale.Length; i++)
        {
            Assert.True(scale[i] < scale[i - 1], $"Type scale not strictly descending at index {i}");
        }
    }

    // DESIGN.md §4's reworked scale: Display is the largest thing on a screen and
    // outranks the countdown, which the pre-rework scale had the other way round.
    [Fact]
    public void DisplayIsTheLargestScreenSize()
    {
        Assert.Equal(44f, DesignTokens.TypeSp.Display);
        Assert.True(DesignTokens.TypeSp.Display > DesignTokens.TypeSp.Countdown);
    }

    [Fact]
    public void TypeScaleMatchesTheDesignBible()
    {
        Assert.Equal(44f, DesignTokens.TypeSp.Display);
        Assert.Equal(28f, DesignTokens.TypeSp.Title);
        Assert.Equal(18f, DesignTokens.TypeSp.Heading);
        Assert.Equal(16f, DesignTokens.TypeSp.Body);
        Assert.Equal(14f, DesignTokens.TypeSp.Secondary);
        Assert.Equal(13f, DesignTokens.TypeSp.Label);
        Assert.Equal(14f, DesignTokens.TypeSp.Mono);
        Assert.Equal(26f, DesignTokens.TypeSp.Voice);
        Assert.Equal(40f, DesignTokens.TypeSp.Countdown);
        Assert.Equal(72f, DesignTokens.TypeSp.CoverTitle);
    }

    // All six faces ship in every APK via the Fonts/*.ttf glob; Serif Black and
    // Pinyon were packaged but unreachable, so Display could not be set in Black
    // and the owner's voice had no hand to be written in.
    [Fact]
    public void EveryPackagedFaceIsDeclared()
    {
        var declared = new[]
        {
            DesignTokens.FontAssets.SerifRegular,
            DesignTokens.FontAssets.SerifSemibold,
            DesignTokens.FontAssets.SerifBlack,
            DesignTokens.FontAssets.SerifItalic,
            DesignTokens.FontAssets.SansRegular,
            DesignTokens.FontAssets.SansSemibold,
            DesignTokens.FontAssets.Mono,
            DesignTokens.FontAssets.Script,
        };

        var fonts = Path.Combine(DesignTestHelpers.FocusPhoneRoot(), "..", "Fonts");
        foreach (var asset in declared)
        {
            var file = Path.Combine(fonts, Path.GetFileName(asset));
            Assert.True(File.Exists(file), $"{asset} is declared but {file} does not exist");
        }

        Assert.Equal(
            Directory.EnumerateFiles(fonts, "*.ttf").Select(file => Path.GetFileName(file)).Order(),
            declared.Select(asset => Path.GetFileName(asset)).Order());

        Assert.Equal(declared.Length, declared.Distinct().Count());
        Assert.All(declared, asset => Assert.StartsWith("fonts/", asset));
    }

    [Fact]
    public void MonoMatchesSecondarySize()
    {
        Assert.Equal(DesignTokens.TypeSp.Secondary, DesignTokens.TypeSp.Mono);
    }

    [Fact]
    public void CornerRadiiAreSoftStationery()
    {
        // DESIGN.md §4: soft-cornered stationery, radii 10–16, never pills.
        var radii = new[]
        {
            DesignTokens.Shape.RadiusInput,
            DesignTokens.Shape.RadiusButton,
            DesignTokens.Shape.RadiusCard,
        };
        foreach (var radius in radii)
        {
            Assert.InRange(radius, 10, 16);
        }
    }

    [Fact]
    public void MotionUsesTheTwoSanctionedDurations()
    {
        Assert.Equal(150, DesignTokens.Motion.QuickMs);
        Assert.Equal(200, DesignTokens.Motion.StandardMs);
        Assert.True(DesignTokens.Motion.UndoWindowMs >= 5_000, "undo window must be long enough to read and react");
        Assert.InRange(DesignTokens.Motion.CutoverCountdownSeconds, 8, 15);
    }

    [Fact]
    public void SpacingScaleIsMultiplesOfFourAndAscending()
    {
        var scale = new[]
        {
            DesignTokens.Space.X1, DesignTokens.Space.X2, DesignTokens.Space.X3,
            DesignTokens.Space.X4, DesignTokens.Space.X6, DesignTokens.Space.X8,
            DesignTokens.Space.X12, DesignTokens.Space.X16, DesignTokens.Space.X24,
        };
        for (var i = 0; i < scale.Length; i++)
        {
            Assert.True(scale[i] % 4 == 0, $"Spacing {scale[i]} is not a multiple of 4");
            if (i > 0)
            {
                Assert.True(scale[i] > scale[i - 1], "Spacing scale not ascending");
            }
        }
    }

}
