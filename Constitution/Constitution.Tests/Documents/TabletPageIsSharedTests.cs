using Constitution.Domain.Documents;
using Storyworthtelling.Paper;

namespace Constitution.Tests.Documents;

/// <summary>
/// Constitution's page is the suite's registered reMarkable page (audit 01 §12). The Domain stays free of the drawing
/// library, so it keeps the two numbers and this holds them to the shared ones.
/// </summary>
public class TabletPageIsSharedTests
{
    [Fact]
    public void ThePageIsTheRegisteredTabletPage()
    {
        Assert.Equal(RemarkablePage.Width, ConstitutionPage.Width);
        Assert.Equal(RemarkablePage.Height, ConstitutionPage.Height);
    }
}
