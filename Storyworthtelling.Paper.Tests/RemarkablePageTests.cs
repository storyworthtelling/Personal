using Storyworthtelling.Paper;

namespace Storyworthtelling.Paper.Tests;

// One registered page for the tablet (audit 01 §12): the native 3:4 of the glass in points, so a point on the page is a
// point on the screen and a vision prompt can name a region on it without scaling.
public class RemarkablePageTests
{
    [Fact]
    public void ThePageIsTheTabletsOwnThreeByFour()
    {
        Assert.Equal(445f, RemarkablePage.Width);
        Assert.Equal(594f, RemarkablePage.Height);
        Assert.Equal(0.75f, RemarkablePage.Width / RemarkablePage.Height, 2);
    }
}
