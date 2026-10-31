using System.Text.RegularExpressions;

namespace Constitution.Tests.Today;

/// <summary>
/// D43 header fix: the date sits in the page header (an area's Subtitle), not lettered bare on the
/// crinkled ground. The page's only lettered thing is on a scrap.
/// </summary>
public class TodayHeaderSubtitleTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative));

    [Fact]
    public void TodayPageNoLongerLettersTheDate()
    {
        var page = Read("Constitution.Android/Today/TodayPage.cs");

        Assert.DoesNotContain("TodayModel.Header", page);
        Assert.DoesNotContain("new TextView", page.Replace("var plain = new TextView(context)", string.Empty),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TodayPageLettersOnlyThroughTheScrap()
    {
        var page = Read("Constitution.Android/Today/TodayPage.cs");
        var views = Regex.Matches(page, @"new TextView\(").Count;

        // The one TextView is the plain top line, handed to the notepad leaf scrap and never added to the page.
        Assert.Equal(1, views);
        Assert.Contains("ScrapKind.NotepadLeaf", page);
        Assert.DoesNotMatch(new Regex(@"AddView\(\s*(date|plain|line)\b"), page);
    }

    [Fact]
    public void TheAreaInterfaceCarriesAnOptionalSubtitle()
    {
        Assert.Matches(new Regex(@"string\?\s+Subtitle"), Read("Constitution.Android/IConstitutionArea.cs"));
    }

    [Fact]
    public void TodaySetsItsSubtitleFromTheHeaderModel()
    {
        var area = Read("Constitution.Android/TodayArea.cs");

        Assert.Matches(new Regex(@"Subtitle[\s\S]{0,80}TodayModel\.Header\(DateTime\.Now\)"), area);
    }

    [Fact]
    public void TheShellRendersTheSubtitleUnderTheHeaderOutsideTheWell()
    {
        var shell = Read("Constitution.Android/ConstitutionActivity.cs");

        Assert.Contains("active.Subtitle", shell);
    }
}
