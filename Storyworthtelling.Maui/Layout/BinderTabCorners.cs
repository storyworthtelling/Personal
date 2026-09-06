namespace Storyworthtelling.Maui.Layout;

/// <summary>
/// Which corners of a binder tab are rounded, named rather than ordered.
///
/// <para>The rule is one line of DESIGN.md §9 — a tab is rounded on the <b>outer</b> side only, so
/// it reads as the page continuing under the seam rather than as a chip floating beside it — and
/// it is worth a type because every framework states corners in its own order. The native painter
/// passes eight floats as x/y pairs starting top-left; MAUI takes a <c>CornerRadius</c> whose
/// constructor order is not something to recall from memory. Naming them here means the single
/// call site reads as the rule instead of as four bare numbers.</para>
/// </summary>
public readonly record struct BinderTabCorners(
    double TopLeft, double TopRight, double BottomLeft, double BottomRight)
{
    /// <summary>Rounded away from the page: the right-hand edge of a right-edge gutter.</summary>
    public static BinderTabCorners Outer(double radius) => new(0, radius, 0, radius);
}
