namespace Index.Domain.Design;

/// <summary>
/// The three materials Accounts is drawn on (INDEX-P11-PLAN.md Release B, D122/D126/D127/D129/D131/
/// D141): graph paper underneath the page, an index card for every account, and a post-it for the
/// account a tap has opened. A registered token file (DESIGN.md §16) — the leak guard
/// (<c>HexLiteralLeakTests</c>) allowlists this file for exactly that reason, the same way
/// <c>Tools/IconStudio/Tokens.cs</c> is allowlisted for the identity ladder and
/// <c>LedgerAreas.cs</c> is allowlisted for the five tabs' hues.
///
/// <para>These are content/material colours, not Paper &amp; Ink roles — a note-card's cream and a
/// post-it's yellow are what the mockups draw regardless of role, the same way
/// <c>Storyworthtelling.Paper.MaterialTokens</c> holds the desk wood and shelf bronze for the PDF
/// renderers. This is that file's MAUI-screen counterpart: plain hex strings and numeric constants
/// (no <c>SkiaSharp</c>/<c>Microsoft.Maui.Graphics</c> type), so <c>Index.Domain</c> stays free of a
/// UI toolkit dependency and a view converts a hex string the ordinary way
/// (<c>Color.FromArgb(MaterialTokens.NoteCardHex)</c>).</para>
/// </summary>
public static class MaterialTokens
{
    // The index card's stock (D126) — a cream close to the page but not the page, so a card reads as
    // a separate sheet lying on the graph paper rather than a cut-out of it.
    public const string NoteCardHex = "#FFFEF8";

    // The index card's two rule colours (D129): a red head rule under the title (mockup-02's `.ix-hd`
    // border), and pale blue body rules the account's lines are written on, at a fixed pitch — the
    // mockup's CSS repeating-linear-gradient step (mockup-02.html, `.ix-body`).
    public const string CardHeadRuleHex = "#E4A7A2";
    public const string CardBodyRuleHex = "#D6E3EF";
    public const decimal CardRulePitchDp = 22m;

    // The ordinary post-it (D127) — a hand-entered account, or a bank Plaid gives no colour for.
    // Index.Domain.Design.PostItTint.From returns this exact hex whenever there is no bank colour to
    // tint, so nothing on a screen ever hardcodes this value a second time.
    public const string PostItHex = "#FBF3C9";
    public const string PostItEdgeHex = "#E4D79A";

    // Graph paper (D122): two tiers of line in the identity ground colour (LedgerAreas.IdentityGroundHex),
    // not a new hex — an ALPHA fraction composited onto that colour, so the guard
    // (GraphPaperContrastTests) is checking the actual composited result rather than a flat value that
    // is never what the screen draws. 14% is the measured contrast ceiling: any darker and muted ink
    // drops under 3:1. Kept as decimal (not float/double) so MoneyIsDecimalTests' whole-assembly scan
    // for stray floating-point Domain members has nothing to flag here — these are alpha fractions,
    // not money, but the guard does not know that and there is no reason to give it an exception to
    // remember.
    public const decimal GraphPaperMinorAlpha = 0.08m;
    public const decimal GraphPaperMajorAlpha = 0.14m;
    public const int GraphPaperMinorDp = 16;
    public const int GraphPaperMajorDp = 64;

    // The faint identity tint a required field carries (DESIGN.md §7; mockup 4/15's `.req`:
    // rgba(182,195,169,.22)) — an ALPHA over the Accounts identity cloth, never a second hex. Decimal for
    // the same reason the graph-paper alphas are.
    public const decimal RequiredTintAlpha = 0.22m;

    // The haze an action card is set over (mockup 2/4's `.dv-haze`): the page's PaperDeep at 86%, over the
    // WHOLE page — header, well, binder and pinned bar alike — so the card is the only thing left clear.
    // An alpha over a kit colour, never a second hex; decimal for the same reason the other alphas are.
    public const decimal HazeAlpha = 0.86m;
}
