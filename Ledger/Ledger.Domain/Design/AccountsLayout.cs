namespace Index.Domain.Design;

/// <summary>
/// The spacing the Accounts family of screens is drawn at (Phase 4, "Spacing"), read off the mockups' CSS
/// rather than guessed at the page: <c>.ix{margin-top:8px}</c>, <c>.dv-inst{margin-top:22px}</c>,
/// <c>.pi{margin:-6px 4px 4px 22px}</c>, <c>.dv-well{padding:28px 16px 0 24px}</c>,
/// <c>.dv-card{left:24px;right:90px}</c>, <c>.dv-empty{margin-top:180px}</c>. Plain numbers, in dp — layout,
/// not a colour or a type size — so a page names them instead of keeping a private copy that drifts.
/// </summary>
public static class AccountsLayout
{
    // Between one card and the next, and from a bank's heading to its first card.
    public const int CardGapDp = 8;
    public const int HeadingToCardDp = 8;

    // Above a bank's heading: the gap between one bank's group and the next.
    public const int GroupMarginDp = 22;

    // The well the cards lie in: 24 from the page's left edge, 16 from the binder's side.
    public const int WellPaddingLeftDp = 24;
    public const int WellPaddingRightDp = 16;

    // The post-it overlaps the foot of its card (a negative top) and is indented from the card's left edge.
    public const int PostItMarginLeftDp = 22;
    public const int PostItMarginTopDp = -6;
    public const int PostItMarginRightDp = 4;
    public const int PostItMarginBottomDp = 4;

    // "Remove this account": a 48dp target, with a rule above it.
    public const int RemoveHitDp = 48;

    // An action card over the haze sits 24 from the left; on the right it clears the 66dp binder plus 24.
    public const int ActionCardLeftDp = 24;
    public const int ActionCardRightDp = 90;

    // First-run: the empty state starts 180 below the header rule, and its sentence is 270 wide at most.
    public const int EmptyStateTopDp = 180;
    public const int EmptyStateSentenceWidthDp = 270;

    // A boxed field is 44dp tall (mockup 4: `.ixg-box{height:44px}`).
    public const int FieldHeightDp = 44;

    /// <summary>
    /// What a block's top margin comes to when it follows a post-it. In the mockups' flow layout the post-it's
    /// own 4dp bottom margin and the next block's top margin collapse to the larger of the two; a stack adds
    /// them, so the next block is given only the part of its margin the post-it has not already supplied.
    /// </summary>
    public static int GapAfterPostIt(int nextMarginTop) =>
        Math.Max(nextMarginTop, PostItMarginBottomDp) - PostItMarginBottomDp;
}
