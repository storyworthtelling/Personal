using Storyworthtelling.Maui.Layout;

namespace Storyworthtelling.Maui.Tests;

public class BinderTabCornersTests
{
    /// <summary>
    /// A tab is rounded on the outer side only. Rounding the page side would close the tab off
    /// from the content, which is the opposite of what a divider does — and it is the mistake a
    /// misremembered constructor order makes, silently, since four numbers in the wrong sequence
    /// still draw a plausible-looking shape.
    /// </summary>
    [Fact]
    public void ATabIsRoundedAwayFromThePageAndSquareAgainstIt()
    {
        var corners = BinderTabCorners.Outer(12);

        Assert.Equal(12, corners.TopRight);
        Assert.Equal(12, corners.BottomRight);
        Assert.Equal(0, corners.TopLeft);
        Assert.Equal(0, corners.BottomLeft);
    }
}
