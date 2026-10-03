namespace Index.Domain.Design;

/// <summary>A margin in dp: left, top, right, bottom (a MAUI <c>Thickness</c> without the toolkit).</summary>
public readonly record struct PlanInset(double Left, double Top, double Right, double Bottom);

/// <summary>
/// The measures Snowball, Budget and Savings are laid out on (Phase 4, "Spacing"; mockups 6, 7, 8, 9, 16),
/// read off the mockups' own CSS rather than chosen: cards 8dp apart (<c>.ix{margin-top:8px}</c>), a heading
/// 8dp above its card, a 22dp margin between groups (<c>.dv-inst{margin-top:22px}</c>), the well padded
/// <c>28 16 0 24</c> (<c>.dv-well</c>), and a post-it stuck on with <c>margin:-6px 4px 4px 22px</c>
/// (<c>.pi</c>). Numbers, not colours, so they live beside the other design constants rather than in a token
/// file.
/// </summary>
public static class PlanSpacing
{
    public const double CardGapDp = 8;
    public const double HeadingToCardDp = 8;
    public const double GroupMarginDp = 22;
    public const double WellLeftDp = 24;
    public const double WellRightDp = 16;

    // The post-it's CSS margin, top / right / bottom / left.
    private const double PostItTopDp = -6;
    private const double PostItRightDp = 4;
    private const double PostItBottomDp = 4;
    private const double PostItLeftDp = 22;

    /// <summary>
    /// The post-it's margin inside a stack that already puts <paramref name="stackSpacing"/> between its
    /// children. The stack's gap is part of what the post-it must pull back over the card, and the post-it's
    /// own bottom margin collapses into the gap before the next card — which is what adjacent CSS margins do,
    /// and what the mockup's <c>-6 4 4 22</c> was measured with.
    /// </summary>
    public static PlanInset PostItMargin(double stackSpacing) => new(
        PostItLeftDp,
        PostItTopDp - stackSpacing,
        PostItRightDp,
        Math.Max(0, PostItBottomDp - stackSpacing));
}
