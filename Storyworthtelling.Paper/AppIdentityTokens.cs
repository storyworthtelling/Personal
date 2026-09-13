using SkiaSharp;

namespace Storyworthtelling.Paper;

/// <summary>
/// Each app's identity hue, for the things a <b>printed page</b> signs with it: the rule under a
/// document's header, the app's name in the owner's hand, a section tab's cloth.
///
/// <para>A registered token file (DESIGN.md §16). It exists because the identity hues were declared
/// in <c>Tools/IconStudio/Tokens.cs</c> — which is registered, and is the right place for them,
/// because that console is what paints every icon — and a console application is not something an
/// Infrastructure assembly can reference. Rather than copy three hexes into a composer, where the
/// leak guard would rightly stop them, they are declared here and a parity test holds the two files
/// to the same values.</para>
///
/// <para>These are <b>not</b> the role palette. A role says what something means; an identity says
/// whose it is. Nothing here may stand in for Action, Caution or any other role — Foreword's
/// sunrise sits 30° clear of the nearest chromatic role precisely so the two are never confused.</para>
/// </summary>
public static class AppIdentityTokens
{
    // Foreword: sunrise, OKLCH hue 47°, locked at gate 3. The fill tier is the cloth an inactive
    // binder tab is cut from (§9) — the one tier a SCREEN needs, which is why it arrived later than
    // the three the printed page uses.
    public const string ForewordFillHex = "#E8B99A";
    public const string ForewordGroundHex = "#DFA277";
    public const string ForewordSunriseHex = "#CA662D";
    public const string ForewordDeepHex = "#6E3512";


    // Folio: olive, OKLCH hue 107°, locked at gate 3. Committed at the CENTRE of the 105–109 window
    // rather than at the art's own 105.4°, which clears the caution ochre by only 20.4°: a re-crop or
    // a regenerated source would drift a measured value under §3's 20° binding and break the build
    // with nothing visibly different. By gate 3 the wheel had four gaps left, two too narrow to use —
    // this is the only window Folio fits.
    public const string FolioFillHex = "#C1C197";
    public const string FolioGroundHex = "#919059";
    public const string FolioMarkHex = "#6B681E";
    public const string FolioTextHex = "#575410";
    public const string FolioDeepHex = "#3B3902";

    // The binding, and deliberately NOT the identity: §12 does not require a cloth to match the
    // identity hue, and this red sits about 3° from the problem rose — fine for a binding, and
    // disqualifying for an identity token. IdentityLadderTests reads the ladder, never this.
    public const string FolioClothHex = "#B32738";

    public static SKColor ForewordFill { get; } = SKColor.Parse(ForewordFillHex);
    public static SKColor ForewordGround { get; } = SKColor.Parse(ForewordGroundHex);
    public static SKColor ForewordSunrise { get; } = SKColor.Parse(ForewordSunriseHex);
    public static SKColor ForewordDeep { get; } = SKColor.Parse(ForewordDeepHex);

    public static SKColor FolioFill { get; } = SKColor.Parse(FolioFillHex);
    public static SKColor FolioGround { get; } = SKColor.Parse(FolioGroundHex);
    public static SKColor FolioMark { get; } = SKColor.Parse(FolioMarkHex);
    public static SKColor FolioText { get; } = SKColor.Parse(FolioTextHex);
    public static SKColor FolioDeep { get; } = SKColor.Parse(FolioDeepHex);
    public static SKColor FolioCloth { get; } = SKColor.Parse(FolioClothHex);
}
