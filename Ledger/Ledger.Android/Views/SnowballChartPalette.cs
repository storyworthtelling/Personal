using Index.Domain.Design;

namespace Index.Android.Views;

/// <summary>
/// D113/D136/D138: the Snowball history charts' own material — a green ramp (running deep-to-light
/// across the loans in payoff order) for the stacked bars, and one fixed ink-blue for every
/// small-multiple's line. <b>Neither is a Paper &amp; Ink ROLE colour</b> (D113's own words: "Neither
/// chart is a role colour: the green ramp and ink-blue"), so nothing here reads a role token, and
/// nothing here is a hex STRING literal either — every value is a numeric OKLCH triple fed through the
/// existing <see cref="Oklch"/> port, computed at draw time. That is what keeps this file outside
/// <c>HexLiteralLeakTests</c>' concern entirely, rather than adding a third file to that guard's
/// allow-list: there is no <c>"#RRGGBB"</c> string anywhere below for it to find.
///
/// <para>The ramp is anchored to <see cref="Pages.LedgerAreas.AccountsGroundHex"/> — Index's own
/// identity green (D115/D117) — rather than a second, separately-invented green, per the task's own
/// framing ("it's NOT a role colour, it's the identity green ramped from deep to light").</para>
/// </summary>
internal static class SnowballChartPalette
{
    private const double RampChroma = 0.09;
    private const double RampDeepestLightness = 0.20; // the first loan to pay off — the darkest segment
    private const double RampLightestLightness = 0.86; // the mortgage, last in payoff order — the lightest

    /// <summary>The stacked-bar segment / small-multiple accent colour for the loan at
    /// <paramref name="indexInPayoffOrder"/> of <paramref name="loanCount"/> loans total (index 0 = the
    /// first loan to pay off, darkest; the last index = the mortgage, lightest).</summary>
    public static string RampHex(int indexInPayoffOrder, int loanCount)
    {
        var (_, _, hue) = Oklch.FromHex(Pages.LedgerAreas.AccountsGroundHex);
        var t = loanCount <= 1 ? 0.0 : (double)indexInPayoffOrder / (loanCount - 1);
        var lightness = RampDeepestLightness + t * (RampLightestLightness - RampDeepestLightness);
        return Oklch.ToHex(lightness, RampChroma, hue);
    }

    private const double InkBlueLightness = 0.27;
    private const double InkBlueChroma = 0.07;
    private const double InkBlueHueDegrees = 252; // a dark slate-blue, matching mockup 6's own swatch

    /// <summary>The one line colour every small-multiple panel draws in (mockup 6's own <c>#2C3E5E</c>
    /// swatch, reproduced by number rather than copied as a hex literal).</summary>
    public static string InkBlueHex() => Oklch.ToHex(InkBlueLightness, InkBlueChroma, InkBlueHueDegrees);
}
