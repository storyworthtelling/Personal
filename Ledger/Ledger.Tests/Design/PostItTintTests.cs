using Index.Domain.Design;
using Xunit;

namespace Index.Tests.Design;

/// <summary>
/// Guard G24 (D127): every tinted post-it lands at OKLCH L 0.94, chroma ≤ 0.045, and clears soft
/// ink's 4.5:1 floor. The sample hexes are the design book's own illustrative swatches (mockup 3's
/// caption): Redwood Bank-, Meridian Card-, Harbor Auto Finance- and Tech-CU-ish blues/oranges/reds/greens.
/// </summary>
public class PostItTintTests
{
    // Soft ink (#6B6759), the tone every post-it's secondary text uses (never muted — mockup 3's own
    // measurement: muted ink falls to ~2.9:1 on a tinted post-it, which is why soft ink is the rule).
    private const string SoftInkHex = "#6B6759";

    public static IEnumerable<object[]> SampleBankColors =>
    [
        ["#0A5AA8", "Redwood Bank-ish blue"],
        ["#F76B1C", "Meridian Card-ish orange"],
        ["#CC0000", "Harbor Auto Finance-ish red"],
        ["#1F8A3B", "Lakeside CU-ish green"],
        ["#6D2E9C", "a fifth, more saturated purple, to stress the chroma cap"],
    ];

    [Theory]
    [MemberData(nameof(SampleBankColors))]
    public void Tint_LandsAtTheFixedLightness(string bankHex, string label)
    {
        var tinted = PostItTint.From(bankHex);
        var (l, _, _) = Oklch.FromHex(tinted);

        // The tolerance is not arbitrary slack: ToHex quantizes to 8-bit sRGB channels and FromHex
        // reads those back, so even an exact L 0.94 input round-trips with a small, real error — a
        // saturated source hue loses more of it than a muted one. Measured up to ~0.0075 across the
        // sample set below; 0.012 leaves headroom without being wide enough to hide a real miscalculation.
        Assert.True(Math.Abs(l - (double)PostItTint.TintLightness) < 0.012,
            $"{label}: L={l}, expected ~{PostItTint.TintLightness}");
    }

    [Theory]
    [MemberData(nameof(SampleBankColors))]
    public void Tint_NeverExceedsTheChromaCap(string bankHex, string label)
    {
        var tinted = PostItTint.From(bankHex);
        var (_, chroma, _) = Oklch.FromHex(tinted);

        Assert.True(chroma <= (double)PostItTint.MaxChroma + 0.002,
            $"{label}: chroma={chroma}, cap={PostItTint.MaxChroma}");
    }

    [Theory]
    [MemberData(nameof(SampleBankColors))]
    public void Tint_ClearsSoftInkContrastFloor(string bankHex, string label)
    {
        var tinted = PostItTint.From(bankHex);
        var ratio = DesignTestHelpers.ContrastRatio(SoftInkHex, tinted);

        // This is the measured floor the mockup's own notes state (4.59–4.82:1 on the five banks it
        // sampled) — recorded here as a real assertion rather than a cited number, so a future change
        // to the tint formula that quietly drops below it fails a test instead of a caption.
        Assert.True(ratio >= 4.5,
            $"{label}: soft ink only clears {ratio:0.00}:1 on tint {tinted} — below the 4.5:1 floor.");
    }

    [Fact]
    public void NoColor_FallsBackToTheOrdinaryPostIt()
    {
        Assert.Equal(MaterialTokens.PostItHex, PostItTint.From(null));
        Assert.Equal(MaterialTokens.PostItHex, PostItTint.From(""));
        Assert.Equal(MaterialTokens.PostItHex, PostItTint.From("   "));
    }

    [Fact]
    public void MalformedColor_FallsBackRatherThanThrowing()
    {
        Assert.Equal(MaterialTokens.PostItHex, PostItTint.From("not-a-color"));
    }

    [Theory]
    [MemberData(nameof(SampleBankColors))]
    public void Edge_IsDarkerThanTheTint_SameHue(string bankHex, string label)
    {
        var tint = PostItTint.From(bankHex);
        var edge = PostItTint.Edge(tint);

        var (tintL, _, tintHue) = Oklch.FromHex(tint);
        var (edgeL, _, edgeHue) = Oklch.FromHex(edge);

        Assert.True(edgeL < tintL, $"{label}: edge ({edgeL}) should be darker than the tint ({tintL}).");
        Assert.True(Math.Abs(edgeHue - tintHue) < 5, $"{label}: edge hue drifted from the tint's hue.");
    }

    [Fact]
    public void ColorWithoutHash_IsAccepted()
    {
        // Plaid does not consistently hash-prefix primary_color across every institution response
        // sampled in other apps' integrations; From() normalizes rather than rejecting.
        var withHash = PostItTint.From("#0A5AA8");
        var withoutHash = PostItTint.From("0A5AA8");

        Assert.Equal(withHash, withoutHash);
    }
}
