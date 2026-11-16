using Storyworthtelling.Materials;
using Storyworthtelling.Paper;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Index.Android.Pages;

/// <summary>Which of Index's five areas a screen belongs to. The order is the strip's order and the
/// app's tab order (mockup 2). Release B renamed the old two-tab <c>Balances</c>/<c>Settings</c> shape
/// to the five the design book settles on (D118, D120): Accounts, Snowball, Budget, Savings,
/// Settings.</summary>
public enum LedgerArea
{
    Accounts,
    Snowball,
    Budget,
    Savings,
    Settings,
}

/// <summary>
/// Index's five binder areas: the label, the glyph, and the two colours DESIGN.md §9 gives a tab
/// (D120) — one hue each, so the strip reads as five separate dividers rather than a row of
/// interchangeable buttons.
///
/// <para><b>Registered token file</b> (DESIGN.md §16), allowlisted in <c>HexLiteralLeakTests</c> for
/// the same reason <c>Tools/IconStudio/Tokens.cs</c> is: these hex values ARE the identity ladder
/// D115/D117 committed there for IconStudio's own use, and the three working-area hues D120 fixes for
/// this build alone. <c>Ledger.Android</c>/<c>Ledger.Domain</c> take no project reference on
/// <c>Tools/IconStudio</c> for this — wrong direction, wrong layer — so the values are retyped here as
/// literals rather than shared by reference. <see cref="Ledger.Tests.Design.AreaHueTests"/> (Guard G9)
/// reads this file's source text (the same way <c>KitAdoptionTests</c> does) to check the hue-
/// separation rule without loading a MAUI assembly a plain <c>net10.0</c> test project cannot
/// reference.</para>
///
/// <para><b>Accounts</b> carries the identity hue and is the one named, recorded exception to the
/// suite's role-separation rule: D115/D116 sit it at 0° from the Positive role on purpose (§3), so
/// Index's own pages draw no Positive-role hue anywhere (nothing here does either — these are all
/// area/identity hues, never role hex). <b>Settings</b> keeps the plain neutral pair every app in the
/// suite uses for it, read from the kit rather than retyped, exactly as before this release.</para>
///
/// <para>The glyphs are the suite's one set, <c>Storyworthtelling.Materials.TabGlyphs</c>, drawn in colored
/// pencil by the kit's strip (DESIGN.md §10, W8.8).</para>
/// </summary>
public static class LedgerAreas
{
    // ---- D115/D117: the bookplate-green identity, Storyworthtelling.Paper.AppIdentityTokens' Index ladder, and D120's
    // three role hues, PaperColorTokens' tiers. Named, never retyped (audit W8.5, report 04 §6: a second hand-typed
    // copy of the palette is the failure DESIGN.md §19 describes). ----
    private const string AccountsMarkHex = AppIdentityTokens.IndexMarkHex;
    private const string AccountsFillHex = AppIdentityTokens.IndexFillHex;

    // D122's graph paper is drawn in this same identity colour (mockup-02's background-image is not a
    // second, separately-chosen green).
    public const string AccountsGroundHex = AppIdentityTokens.IndexGroundHex;

    // ---- D120: Snowball's in-progress teal. ----
    private const string SnowballMarkHex = PaperColorTokens.ProgressMark;
    private const string SnowballFillHex = PaperColorTokens.ProgressFill;

    // ---- D120: Budget's caution ochre. ----
    private const string BudgetMarkHex = PaperColorTokens.CautionMark;
    private const string BudgetFillHex = PaperColorTokens.CautionFill;

    // ---- D120: Savings' reference slate-blue. ----
    private const string SavingsMarkHex = PaperColorTokens.ReferenceMark;
    private const string SavingsFillHex = PaperColorTokens.ReferenceFill;

    /// <summary>
    /// The rule under a screen's header: its area's colour, and the neutral grey on Settings — a
    /// settings screen is grey in every app in the suite, so it never looks like somewhere you are.
    /// </summary>
    public static Color RuleInk(LedgerArea area) => All[(int)area].Hue;

    public static IReadOnlyList<BinderArea> All => [
        new("Accounts", TabGlyphs.Bank, Color.FromArgb(AccountsMarkHex), Color.FromArgb(AccountsFillHex)),
        new("Snowball", TabGlyphs.Staircase, Color.FromArgb(SnowballMarkHex), Color.FromArgb(SnowballFillHex)),
        new("Budget", TabGlyphs.Ledger, Color.FromArgb(BudgetMarkHex), Color.FromArgb(BudgetFillHex)),
        new("Savings", TabGlyphs.MoneyBag, Color.FromArgb(SavingsMarkHex), Color.FromArgb(SavingsFillHex)),
        new("Settings", TabGlyphs.Settings, PaperResources.Colour("ResolvedMark"), PaperResources.Colour("ResolvedFill")),
    ];
}
