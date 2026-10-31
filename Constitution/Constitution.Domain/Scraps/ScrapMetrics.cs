using Codex.Domain.Design;

namespace Constitution.Domain.Scraps;

/// <summary>
/// The numbers behind the scrap page, each read off the page-11-to-16 mockups' own CSS (the
/// approved design artifact for P13; line numbers below are that file's) rather than chosen. The
/// painters draw to these and the layout places by them, so the page and the picture cannot drift.
/// </summary>
public static class ScrapMetrics
{
    /// <summary>
    /// The binder gutter down the right edge, 66 dp (mockup: <c>.dv-well{right:66px}</c>, line 180).
    /// <c>BinderTabs.WidthDp</c> in <c>Codex.Ui.Android</c> is the suite's source for it; Domain
    /// cannot reference Android code, so a test holds this copy equal by reading that file.
    /// </summary>
    public const int BinderGutterDp = 66;

    /// <summary>The well's left padding, 24 dp (<c>.dv-well{padding:28px 18px 0 24px}</c>, line 180).</summary>
    public const int WellPaddingStartDp = DesignTokens.Space.X6;

    /// <summary>The well's right padding, 18 dp (same rule, line 180).</summary>
    public const int WellPaddingEndDp = 18;

    /// <summary>A scrap's side margin, 2 dp (<c>.scrap{margin:22px 2px 6px}</c>, line 190).</summary>
    public const int SideMarginDp = 2;

    /// <summary>Above a scrap, 22 dp (line 190); a chart scrap asks 18 (<c>.scrap.chart{margin-top:18px}</c>, line 321).</summary>
    public const int MarginTopDp = 22;

    /// <summary>Below a scrap, 6 dp (line 190).</summary>
    public const int MarginBottomDp = 6;

    /// <summary>
    /// The smallest tilt a scrap is given, 0.4 degrees. Every inline <c>--t</c> on the scraps of
    /// pages 11 to 16 lies between -1.2deg and 1.0deg, and none is nearer zero than 0.4deg.
    /// </summary>
    public const double MinTiltDegrees = 0.4;

    /// <summary>
    /// The largest tilt, 1.2 degrees (the mockups' <c>--t:-1.2deg</c> on a notepad leaf). The layout
    /// draws magnitudes from 0.4 to 1.2 and leans neighbours opposite ways, so the page reads as
    /// calm (D47), not as a slope.
    /// </summary>
    public const double MaxTiltDegrees = 1.2;

    /// <summary>The mockups write tilts to a tenth of a degree.</summary>
    public const double TiltStepDegrees = 0.1;

    /// <summary>The tape's height, 20 dp (<c>.scrap .tape</c>, line 204).</summary>
    public const double TapeHeightDp = 20;
}
