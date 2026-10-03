namespace Index.Domain.Model;

/// <summary>
/// The disclosure mark on an index card (Phase 4, "Chevrons"; mockups 2 and 3's <c>.ix-cv</c>): a down
/// chevron on a collapsed card and an up chevron on the open one, drawn as a 14 x 14 stroke rather than set
/// as the "▸/▾" text glyphs, which a font is free to draw as anything.
///
/// <para>A card is handed a <i>token</i>. <see cref="Resolve"/> also reads the two old text glyphs, so a
/// card whose page has not moved to the tokens yet still gets the drawn chevron: the old "▸" meant
/// collapsed (now <see cref="Down"/>), the old "▾" meant open (now <see cref="Up"/>).</para>
/// </summary>
public static class CardChevron
{
    public const string Down = "chevron-down";
    public const string Up = "chevron-up";

    /// <summary>The viewbox side, in dp (the mockup's <c>width:14px;height:14px</c>).</summary>
    public const int SizeDp = 14;

    /// <summary>The stroke width (the mockup's <c>stroke-width:1.6</c>).</summary>
    public const decimal StrokeDp = 1.6m;

    private const string LegacyCollapsed = "▸";
    private const string LegacyExpanded = "▾";

    public static string For(bool expanded) => expanded ? Up : Down;

    /// <summary>The SVG path data, in the 14 x 14 box, for a token from <see cref="For"/>.</summary>
    public static string PathOf(string token) => token switch
    {
        Down => "M4 5.5 7 8.5 10 5.5",
        Up => "M4 8.5 7 5.5 10 8.5",
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Not a chevron token."),
    };

    /// <summary>The chevron a token or an old glyph names, or null when it names none (it is then plain text).</summary>
    public static string? Resolve(string? token) => token switch
    {
        Down or LegacyCollapsed => Down,
        Up or LegacyExpanded => Up,
        _ => null,
    };
}
