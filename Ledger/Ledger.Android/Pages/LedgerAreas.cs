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
/// <para>The glyphs are 24-unit fill path data, the kit's convention (see <see cref="BinderArea"/>).
/// Accounts, Snowball, Budget and Savings are filled redraws of the STROKE icons mockup-02.html draws
/// for each tab — the mockup's own convention is an outlined 26-unit stroke icon, and
/// <c>BinderGlyphDrawable</c> only fills a path, so a literal copy of the mockup's <c>d</c> attribute
/// (open, unfilled subpaths) would not render as the same silhouette. Each is redrawn as a solid fill
/// that reads as the same shape (a bank's pediment and columns, a descending staircase, a ruled grid,
/// a tied money bag) at the same 24-unit viewport <see cref="BalancesGlyph"/> already used for the
/// same reason before this release — see that constant's own remark. <b>This substitution is a
/// finding, surfaced rather than silently shipped</b>: a pixel-exact trace of the mockup's stroke path
/// data was not attempted, since the kit has no stroke-drawing glyph type to render it with. Settings
/// reuses the exact path Foreword and Folio draw for theirs verbatim — three sliders, not a cog —
/// since it is one component across the suite and cannot read differently in a sixth app.</para>
/// </summary>
public static class LedgerAreas
{
    // ---- D115/D117: the bookplate-green identity, committed in Tools/IconStudio/Tokens.cs as
    // IndexMarkHex/IndexFillHex. Retyped as literals per this file's own remark above. ----
    private const string AccountsMarkHex = "#596C45";
    private const string AccountsFillHex = "#B6C3A9";

    // D122's graph paper is drawn in this same identity colour (mockup-02's background-image is not a
    // second, separately-chosen green) — public so MainPage can read it without this file's hex
    // literals leaking a second copy into a non-registered file.
    public const string AccountsGroundHex = "#839372";

    // ---- D120: Snowball's in-progress teal. ----
    private const string SnowballMarkHex = "#18717C";
    private const string SnowballFillHex = "#9BC7CD";

    // ---- D120: Budget's caution ochre. ----
    private const string BudgetMarkHex = "#805F00";
    private const string BudgetFillHex = "#D0BB91";

    // ---- D120: Savings' reference slate-blue. ----
    private const string SavingsMarkHex = "#4C6787";
    private const string SavingsFillHex = "#AEC0D5";

    // A pediment roof, a base ledge, and four columns — the fill-path redraw of mockup-02's
    // bank-columns stroke icon (see this class's remarks).
    private const string AccountsGlyph =
        "M2 9 12 3 22 9Z M2 19h20v2H2Z M3.5 9h2v10h-2Z M8.5 9h2v10h-2Z M13.5 9h2v10h-2Z M18.5 9h2v10h-2Z";

    // A descending staircase, filled solid under the steps — the fill-path redraw of mockup-02's
    // Snowball icon (four steps down).
    private const string SnowballGlyph = "M3 6H8V11H13V16H18V21H23V22H3Z";

    // A ruled outer frame with two horizontal dividers and one vertical — the fill-path redraw of
    // mockup-02's Budget grid icon. The frame is a ring (an outer rect with an inner rect of opposite
    // winding, so BinderGlyphDrawable's NonZero fill leaves its middle open) rather than a solid square,
    // so the glyph still reads as a grid and not a filled tile.
    private const string BudgetGlyph =
        "M4 4H22V22H4Z M5.5 5.5V20.5H20.5V5.5Z M4 10h18v1.5H4Z M4 16h18v1.5H4Z M11 4h1.5v18H11Z";

    // A tied neck and a round pouch — the fill-path redraw of mockup-02's Savings icon (D143: the
    // money-bag glyph, replacing an earlier sprout). The mockup's own "$" mark inside the bag is a
    // stroke detail this fill-path redraw does not attempt to reproduce (see this class's remarks) —
    // the bag-and-tie silhouette alone is what the fill convention can carry cleanly.
    private const string SavingsGlyph =
        "M9.5 8 8 4H16L14.5 8Z M12 8C8 8 5 11 5 15C5 18.5 8 21 12 21C16 21 19 18.5 19 15C19 11 16 8 12 8Z";

    // Three sliders, and deliberately not a cog — Foreword settled this for the suite and Folio
    // carried it forward unchanged; a divider that is one component across every app cannot draw
    // differently in the sixth.
    private const string SettingsGlyph =
        "M3 5.2h8v1.8H3zm11.5 0H21v1.8h-6.5zM12 2.6h1.8v7H12zM3 11.1h4v1.8H3zm7.5 0H21v1.8H10.5zM8 "
        + "8.5h1.8v7H8zM3 17h10v1.8H3zm12.5 0H21v1.8h-5.5zM13 14.4h1.8v7H13z";

    /// <summary>
    /// The rule under a screen's header: its area's colour, and the neutral grey on Settings — a
    /// settings screen is grey in every app in the suite, so it never looks like somewhere you are.
    /// </summary>
    public static Color RuleInk(LedgerArea area) => All[(int)area].Hue;

    public static IReadOnlyList<BinderArea> All => [
        new("Accounts", AccountsGlyph, Color.FromArgb(AccountsMarkHex), Color.FromArgb(AccountsFillHex)),
        new("Snowball", SnowballGlyph, Color.FromArgb(SnowballMarkHex), Color.FromArgb(SnowballFillHex)),
        new("Budget", BudgetGlyph, Color.FromArgb(BudgetMarkHex), Color.FromArgb(BudgetFillHex)),
        new("Savings", SavingsGlyph, Color.FromArgb(SavingsMarkHex), Color.FromArgb(SavingsFillHex)),
        new("Settings", SettingsGlyph, PaperResources.Colour("InkMuted"), PaperResources.Colour("PaperDeep")),
    ];
}
