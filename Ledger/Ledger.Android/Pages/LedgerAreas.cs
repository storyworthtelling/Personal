using Storyworthtelling.Maui.Views;

namespace Ledger.Android.Pages;

/// <summary>Which of the two areas a screen belongs to. The order is the strip's order.</summary>
public enum LedgerArea
{
    Balances,
    Settings,
}

/// <summary>
/// Ledger's two binder areas: the label, the glyph, and the two colours DESIGN.md §9 gives a tab.
///
/// <para>Ledger has not adopted the shared Paper &amp; Ink role palette the way Foreword and Folio
/// have (see the repo's CLAUDE.md) — it still carries its own <c>Resources/Styles/DesignTokens.xaml</c>
/// — so unlike <c>ForewordAreas</c>/<c>FolioAreas</c> this reads Ledger's own registered tokens
/// rather than <c>Storyworthtelling.Maui.PaperResources</c>'s role table: <c>Accent</c>/<c>PaperShade</c>
/// for Balances, and <c>InkMuted</c>/<c>PaperDeep</c> — a plain, already-muted pair — for Settings,
/// which is the neutral grey in every app in the suite so it never looks like somewhere you are.
/// Reusing tokens the leak guard already knows about is what keeps this the minimal binder-strip
/// wiring the two pages need rather than the start of a bigger migration.</para>
///
/// <para>The glyphs are 24-unit fill path data, the kit's convention (see <see cref="BinderArea"/>).
/// Settings reuses the exact path Foreword and Folio draw for theirs verbatim — three sliders, not
/// a cog — since it is one component across the suite and cannot read differently in a sixth app.</para>
/// </summary>
public static class LedgerAreas
{
    // Four ledger strokes struck through by a fifth diagonal mark — the fill-path redraw of
    // Tools/IconStudio's Glyphs.Ledger (four ink ticks, one terracotta strike), which is drawn
    // procedurally there and has no path-data form of its own to borrow directly.
    private const string BalancesGlyph =
        "M7 4h1.8v16H7zM10.6 4h1.8v16h-1.8zM14.2 4h1.8v16h-1.8zM17.8 4h1.8v16h-1.8z"
        + "M4.5 15.2 18.3 6.4l1 1.5-13.8 8.8z";

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
        new("Balances", BalancesGlyph, Token("Accent"), Token("PaperShade")),
        new("Settings", SettingsGlyph, Token("InkMuted"), Token("PaperDeep")),
    ];

    // Ledger's own merged Resources/Styles/DesignTokens.xaml, not the kit's PaperResources —
    // see the type's own remarks for why. TryGetValue walks merged dictionaries the way a plain
    // indexer does not; matches the lookup App.xaml.cs already does for "Paper"/"Ink".
    private static Color Token(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color colour
            ? colour
            : throw new InvalidOperationException($"Ledger's DesignTokens.xaml no longer defines '{key}'.");
}
