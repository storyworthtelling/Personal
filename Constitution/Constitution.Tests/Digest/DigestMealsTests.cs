using Constitution.Domain.Documents;

namespace Constitution.Tests.DigestPages;

/// <summary>C-2.6.3 — the meals page is shared content; next week is listed, with ruled lines for notes to the planner.</summary>
public class DigestMealsTests
{
    [Fact]
    public void TheMealsPageIsSharedContent()
    {
        var one = SaturdayDigest.Build(DigestFixture.Model("Sam"));
        var two = SaturdayDigest.Build(DigestFixture.Model("Lee"));
        var a = one[3].Printed.Where(item => !item.Name.StartsWith("footer", StringComparison.Ordinal)).Select(item => (item.Name, item.Text)).ToList();
        var b = two[3].Printed.Where(item => !item.Name.StartsWith("footer", StringComparison.Ordinal)).Select(item => (item.Name, item.Text)).ToList();
        Assert.Equal(a, b);
        Assert.Contains(a, item => item.Text is not null && item.Text.Contains("Lentil soup"));
    }

    [Fact]
    public void TheLovedDishTheStretchAndThreeFacts()
    {
        var page = DigestMeals.MealsPage(DigestFixture.Meals(), DigestFixture.Saturday, 4, 5);
        var text = page.Printed.Select(item => item.Text ?? "").ToList();
        Assert.Contains(text, line => line.Contains("Lentil soup"));
        Assert.Contains(text, line => line.Contains("Braised short ribs") && line.Contains("Fine, a bit rich"));
        Assert.Contains(text, line => line.Contains("Lee will have it again with a salad"));
        Assert.Equal(3, page.Printed.Count(item => item.Name.StartsWith("fact-", StringComparison.Ordinal)));
    }

    [Fact]
    public void NextWeekListsWorkoutsAndDinners()
    {
        var page = DigestMeals.NextWeekPage(DigestFixture.Next(), DigestFixture.Saturday, 5, 5);
        var text = page.Printed.Select(item => item.Text ?? "").ToList();
        Assert.Contains(text, line => line.Contains("Strength"));
        Assert.Contains(text, line => line.Contains("Sheet-pan chicken"));
        Assert.Contains(text, line => line == "Notes to the planner");
    }

    [Fact]
    public void RuledLinesForNotesToThePlanner()
    {
        var page = DigestMeals.NextWeekPage(DigestFixture.Next(), DigestFixture.Saturday, 5, 5);
        var lines = page.Map.Targets.Where(target => target.Id.StartsWith("digest-planner-note-", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, lines.Count);
        Assert.Equal(
            ["digest-planner-note-1", "digest-planner-note-2", "digest-planner-note-3", "digest-planner-note-4"],
            lines.Select(target => target.Id));
        Assert.Equal(lines.Select(line => line.Id), DigestIds.PlannerNotes);
    }

    [Fact]
    public void ReadAtSundaysRun()
    {
        Assert.Equal(new DateOnly(2026, 3, 15), DigestPlannerNotes.ReadOn(DigestFixture.Saturday));
        Assert.False(DigestPlannerNotes.IsDue(DigestFixture.Saturday, DigestFixture.Saturday));
        Assert.True(DigestPlannerNotes.IsDue(DigestFixture.Saturday, new DateOnly(2026, 3, 15)));
        Assert.True(DigestPlannerNotes.IsDue(DigestFixture.Saturday, new DateOnly(2026, 3, 16)));
    }
}
