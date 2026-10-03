namespace Index.Domain.Design;

/// <summary>
/// D127: the post-it's colour. A connected bank's Plaid <c>primary_color</c> — a content colour,
/// like the album art DESIGN.md §7 already carries verbatim, never a palette token — is lightened to
/// OKLCH L 0.94 with its chroma capped at 0.045, so every bank's post-it stays pale and legible no
/// matter how saturated the brand colour is. No bank hex is ever written into the code; this only
/// transforms whatever <see cref="Ledger.Domain.Model.Institution.PrimaryColorHex"/> holds.
///
/// <para>Pure and Plaid-free (testable with plain sample hexes — <c>PostItTintTests</c>, Guard G24),
/// which is the whole point of separating it from the HTTP call that fills the field.</para>
/// </summary>
public static class PostItTint
{
    /// <summary>The post-it's fixed lightness (D127) — pale regardless of the source hue. Kept as
    /// <c>decimal</c> rather than <c>double</c> even though it feeds <see cref="Oklch"/>'s
    /// floating-point math: <c>MoneyIsDecimalTests</c> scans the whole Domain assembly for a stray
    /// <c>double</c>/<c>float</c> member with no exception for "not actually money", so a plain
    /// numeric constant on a public Domain type is easiest kept decimal and cast at the one call
    /// site that needs a double, rather than adding a second named exception to that guard.</summary>
    public const decimal TintLightness = 0.94m;

    /// <summary>The chroma ceiling (D127) — capped, never scaled, so a vivid brand colour is muted
    /// rather than shifted in hue. See <see cref="TintLightness"/>'s remark for why this is
    /// <c>decimal</c>.</summary>
    public const decimal MaxChroma = 0.045m;

    /// <summary>
    /// The tinted post-it hex for <paramref name="bankPrimaryColorHex"/>, or
    /// <see cref="MaterialTokens.PostItHex"/> when there is none or it cannot be parsed — a hand-
    /// entered account, a bank Plaid reports no colour for, or (defensively) a malformed value from
    /// the wire. Never throws: a post-it always has SOME colour to be.
    /// </summary>
    public static string From(string? bankPrimaryColorHex)
    {
        if (string.IsNullOrWhiteSpace(bankPrimaryColorHex))
        {
            return MaterialTokens.PostItHex;
        }

        try
        {
            var normalized = bankPrimaryColorHex.StartsWith('#') ? bankPrimaryColorHex : "#" + bankPrimaryColorHex;
            var (_, chroma, hue) = Oklch.FromHex(normalized);
            var cappedChroma = Math.Min(chroma, (double)MaxChroma);
            return Oklch.ToHex((double)TintLightness, cappedChroma, hue);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return MaterialTokens.PostItHex;
        }
    }

    /// <summary>
    /// A deeper shade of a tinted post-it, for its hairline edge (mockup 3: "a hairline edge in a
    /// deeper shade of its own tint" — every swatch in the mockup's caption pairs a fill with a
    /// visibly darker border of the same hue). Same hue and chroma, lightness stepped down by a fixed
    /// amount — an approximation of the mockup's swatch pairs rather than a traced match to any one
    /// of them, since the mockup gives illustrative pairs, not a formula.
    /// </summary>
    public const decimal EdgeLightnessStep = 0.10m;

    public static string Edge(string tintHex)
    {
        var (l, c, h) = Oklch.FromHex(tintHex);
        return Oklch.ToHex(Math.Max(0, l - (double)EdgeLightnessStep), c, h);
    }
}
