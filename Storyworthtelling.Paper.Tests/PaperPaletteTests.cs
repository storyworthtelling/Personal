using SkiaSharp;
using Storyworthtelling.Paper;

namespace Storyworthtelling.Paper.Tests;

// The device-adaptive colour model approved in the P3 mockup. These lock the two properties the mockup
// promised: every semantic role stays distinct under each profile, and the rM2 grayscale ramp is a
// deliberate, monotonic, separable ramp — not a naive desaturation that would collapse same-luminance hues.
public class PaperPaletteTests
{
    private static readonly PaperRole[] SemanticRoles =
    [
        PaperRole.Active, PaperRole.Prior, PaperRole.New, PaperRole.Problem, PaperRole.Caution,
    ];

    [Theory]
    [InlineData(DeviceColorProfile.PaperPro)]
    [InlineData(DeviceColorProfile.Rm2)]
    [InlineData(DeviceColorProfile.Screen)]
    public void EverySemanticRoleResolvesDistinctlyUnderEachProfile(DeviceColorProfile profile)
    {
        var colours = SemanticRoles.Select(r => PaperPalette.Resolve(r, profile)).ToList();
        Assert.Equal(SemanticRoles.Length, colours.Distinct().Count());
    }

    [Fact]
    public void Rm2RampIsMonotonicFromProblemToCaution()
    {
        // The designed ramp #46 → #A2: problem darkest, caution lightest, strictly increasing so the five
        // greys stay separable side by side. They are true greys (R==G==B), so the red channel is the value.
        var order = new[] { PaperRole.Problem, PaperRole.Active, PaperRole.Prior, PaperRole.New, PaperRole.Caution };
        var values = order.Select(r =>
        {
            var c = PaperPalette.Resolve(r, DeviceColorProfile.Rm2);
            Assert.True(c.Red == c.Green && c.Green == c.Blue, $"{r} is not a true grey under rM2.");
            return c.Red;
        }).ToList();

        for (var i = 1; i < values.Count; i++)
            Assert.True(values[i] > values[i - 1], $"Grey ramp not strictly increasing at index {i}.");
    }

    [Fact]
    public void GroundsDifferByDevice()
    {
        var paperPro = PaperPalette.Resolve(PaperRole.Ground, DeviceColorProfile.PaperPro);
        var rm2 = PaperPalette.Resolve(PaperRole.Ground, DeviceColorProfile.Rm2);
        var screen = PaperPalette.Resolve(PaperRole.Ground, DeviceColorProfile.Screen);

        Assert.Equal(SKColors.White, screen);
        Assert.NotEqual(paperPro, screen);
        Assert.NotEqual(paperPro, rm2);
        Assert.NotEqual(rm2, screen);
    }

    [Fact]
    public void ColourProfilesShareHuesButNotGrounds()
    {
        // Paper Pro desaturation is a property of the Kaleido panel, not the bytes — so the two colour
        // profiles emit identical hues and differ only in the paper ground.
        foreach (var role in SemanticRoles)
        {
            Assert.Equal(
                PaperPalette.Resolve(role, DeviceColorProfile.PaperPro),
                PaperPalette.Resolve(role, DeviceColorProfile.Screen));
        }

        Assert.NotEqual(
            PaperPalette.Resolve(PaperRole.Ground, DeviceColorProfile.PaperPro),
            PaperPalette.Resolve(PaperRole.Ground, DeviceColorProfile.Screen));
    }

    [Fact]
    public void GrayscaleProfileRecoloursSemanticRolesButLeavesInkAlone()
    {
        foreach (var role in SemanticRoles)
        {
            Assert.NotEqual(
                PaperPalette.Resolve(role, DeviceColorProfile.Screen),
                PaperPalette.Resolve(role, DeviceColorProfile.Rm2));
        }

        // Ink is already neutral, so it is the same across every profile.
        Assert.Equal(
            PaperPalette.Resolve(PaperRole.Ink, DeviceColorProfile.Screen),
            PaperPalette.Resolve(PaperRole.Ink, DeviceColorProfile.Rm2));
    }

    [Fact]
    public void ActiveMatchesTheTerracottaAccentInColour()
    {
        Assert.Equal(SKColor.Parse(PaperColorTokens.Accent),
            PaperPalette.Resolve(PaperRole.Active, DeviceColorProfile.PaperPro));
    }
}
