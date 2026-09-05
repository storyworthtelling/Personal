using Storyworthtelling.Paper;
using Xunit;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// The inverse conversion exists so a COMMITTED hex can be checked against the
/// rule that produced it. DESIGN.md §3 requires a per-app identity hue to be
/// separated from the semantic roles it sits near, and that separation is stated
/// in degrees — which is only assertable if a hex can be read back as a hue.
/// Comparing hex strings, which is what the guard used to do, proves nothing.
/// </summary>
public class OklchTests
{
    // Round-tripping is the whole correctness claim: whatever ToHex renders, FromHex
    // must read back. The tolerances are absolute rather than decimal places because
    // the trip goes through 8-bit sRGB, and a quantization error of a few thousandths
    // straddles a rounding boundary often enough to make a places-based assertion a
    // coin flip on values like 0.095.
    [Theory]
    [InlineData(0.50, 0.086, 152.0)]   // Action pine
    [InlineData(0.72, 0.104, 129.0)]   // Positive leaf, a light tier
    [InlineData(0.36, 0.115, 17.0)]    // Problem rose, the darkest tier
    [InlineData(0.50, 0.076, 207.0)]   // InProgress teal
    [InlineData(0.49, 0.095, 285.0)]   // Post indigo
    public void FromHexRoundTripsToHex(double l, double c, double hue)
    {
        var (backL, backC, backH) = Oklch.FromHex(Oklch.ToHex(l, c, hue));

        Assert.InRange(backL, l - 0.005, l + 0.005);
        Assert.InRange(backC, c - 0.005, c + 0.005);
        Assert.InRange(Oklch.HueSeparation(backH, hue), 0.0, 1.0);
    }

    // A near-neutral has no meaningful hue — the angle is numerical noise off a
    // chroma near zero — so callers must be able to see that and not compare it.
    [Fact]
    public void FromHexReportsNearZeroChromaForAGray()
    {
        var (_, chroma, _) = Oklch.FromHex("#808080");

        Assert.True(chroma < 0.01, $"A neutral gray should carry no chroma; got {chroma:F4}.");
    }

    // Separation is measured the short way round the wheel, so 350 and 10 are 20
    // degrees apart rather than 340 — otherwise every hue near the wrap point
    // would look comfortably clear of every other.
    [Theory]
    [InlineData(350.0, 10.0, 20.0)]
    [InlineData(10.0, 350.0, 20.0)]
    [InlineData(178.0, 207.0, 29.0)]   // Score's real separation from the status teal
    [InlineData(100.0, 100.0, 0.0)]
    public void HueSeparationTakesTheShortWayRound(double a, double b, double expected)
    {
        Assert.Equal(expected, Oklch.HueSeparation(a, b), 3);
    }

    // The separation is an angle, never a direction: the roles sit on a wheel and
    // "how far apart" cannot be negative.
    [Fact]
    public void HueSeparationIsNeverNegative()
    {
        for (var a = 0; a < 360; a += 17)
        {
            for (var b = 0; b < 360; b += 23)
            {
                var separation = Oklch.HueSeparation(a, b);
                Assert.InRange(separation, 0.0, 180.0);
            }
        }
    }
}
