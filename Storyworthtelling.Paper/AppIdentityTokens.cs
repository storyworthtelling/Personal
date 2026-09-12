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

    public static SKColor ForewordFill { get; } = SKColor.Parse(ForewordFillHex);
    public static SKColor ForewordGround { get; } = SKColor.Parse(ForewordGroundHex);
    public static SKColor ForewordSunrise { get; } = SKColor.Parse(ForewordSunriseHex);
    public static SKColor ForewordDeep { get; } = SKColor.Parse(ForewordDeepHex);
}
