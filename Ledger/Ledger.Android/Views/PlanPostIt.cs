using Index.Domain.Design;

namespace Index.Android.Views;

/// <summary>
/// Sticks a post-it onto the card above it the way the mockups' <c>.pi</c> does (Phase 4, "Spacing"):
/// overlapping the card's foot and indented from its left edge — <c>margin:-6px 4px 4px 22px</c>. The
/// numbers are <see cref="PlanSpacing.PostItMargin"/>'s, which is told how far apart the surrounding stack
/// already spaces its children, since that gap is part of what the post-it has to pull back over the card.
/// </summary>
public static class PlanPostIt
{
    public static View Place(View postIt, double stackSpacing)
    {
        var margin = PlanSpacing.PostItMargin(stackSpacing);
        postIt.Margin = new Thickness(margin.Left, margin.Top, margin.Right, margin.Bottom);
        return postIt;
    }
}
