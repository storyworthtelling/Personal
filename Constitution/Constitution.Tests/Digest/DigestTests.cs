using Constitution.Domain.Documents;
using Constitution.Domain.ReadBack;

namespace Constitution.Tests.DigestPages;

/// <summary>C-2.6.4 — plain: no voiced line, no calligraphy, gone after three days (R4); five pages; notes margin on pages one to three.</summary>
public class DigestTests
{
    [Fact]
    public void HasNoVoicedLine()
    {
        var pages = SaturdayDigest.Build(DigestFixture.Model());
        Assert.All(pages.SelectMany(page => page.Printed), item => Assert.NotEqual(PrintedRole.Voice, item.Role));
    }

    [Fact]
    public void FivePagesInOrder()
    {
        var pages = SaturdayDigest.Build(DigestFixture.Model());
        Assert.Equal(5, pages.Count);
        Assert.Equal(["Workouts", "Body", "Body", "Meals", "Next week"], pages.Select(page => page.Title));
        Assert.Equal(SaturdayDigest.PageCount, pages.Count);
    }

    [Fact]
    public void PagesOneToThreeKeepTheNotesMargin()
    {
        var pages = SaturdayDigest.Build(DigestFixture.Model());
        for (var index = 0; index < 3; index++)
        {
            Assert.Contains(pages[index].Map.Targets, target => target.Id == "notes");
        }
    }

    [Fact]
    public void OnlyTheLastPageHoldsPlannerLines()
    {
        var pages = SaturdayDigest.Build(DigestFixture.Model());
        var holders = pages.Select((page, index) => (page, index))
            .Where(pair => pair.page.Map.Targets.Any(target => target.Id.StartsWith("digest-planner-note-", StringComparison.Ordinal)))
            .Select(pair => pair.index)
            .ToList();
        Assert.Equal([4], holders);
    }

    [Fact]
    public void GoneAfterThreeDays()
    {
        Assert.Equal(3, ReadBackPolicy.DigestDays);
        Assert.False(ReadBackPolicy.DigestIsDue(DigestFixture.Saturday, DigestFixture.Saturday.AddDays(2)));
        Assert.True(ReadBackPolicy.DigestIsDue(DigestFixture.Saturday, DigestFixture.Saturday.AddDays(3)));
    }

    [Fact]
    public void NoPageNamesAPersonsBirthdate()
    {
        var pages = SaturdayDigest.Build(DigestFixture.Model());
        var text = string.Join(" ", pages.SelectMany(page => page.Printed).Select(item => item.Text)).ToLowerInvariant();
        Assert.DoesNotContain("birth", text);
        Assert.DoesNotContain("born", text);
    }
}
