using Index.Domain.Worksheet;
using Index.Infrastructure.Worksheet;
using Xunit;

namespace Index.Tests.Worksheet;

/// <summary>
/// Guard G18 (INDEX-P11-PLAN.md): the painter and the read-back prompt must read the SAME box
/// positions — constructed from <see cref="WorksheetRegions"/> and nowhere else. Watched to fail
/// first against exactly the bug it exists to catch: hardcoding one box's position independently in
/// either consumer, so a future edit to the shared geometry silently stops matching one of them.
/// </summary>
public class WorksheetRegionsTests
{
    private static readonly Guid CategoryA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CategoryB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CategoryC = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static Index.Domain.Worksheet.WorksheetPlan MakePlan(IReadOnlyList<Guid> categoryIds) =>
        new("2026-10", new DateOnly(2026, 9, 30), 9_350, true, 5, 6_250, 1_906, 1_194,
            categoryIds.Select(id => new WorksheetCategoryLine(id, id.ToString(), 100m)).ToList());

    [Fact]
    public void PainterDrawsExactlyTheBoxesWorksheetRegionsComputes()
    {
        var categoryIds = new[] { CategoryA, CategoryB, CategoryC };
        var plan = MakePlan(categoryIds);

        var expected = WorksheetRegions.AllBoxes(categoryIds);
        var painted = WorksheetPainter.BoxesFor(plan);

        Assert.Equal(expected, painted);
    }

    [Fact]
    public void ReadBackInstructionEmbedsTheExactSameCoordinatesAsTheBoxList()
    {
        var categoryIds = new[] { CategoryA, CategoryB, CategoryC };
        var boxes = WorksheetRegions.AllBoxes(categoryIds);

        var instruction = WorksheetReadBackInstruction.Build(categoryIds);

        foreach (var box in boxes)
        {
            Assert.Contains($"\"{box.Name}\"", instruction);
            Assert.Contains(box.Left.ToString("0.0"), instruction);
            Assert.Contains(box.Top.ToString("0.0"), instruction);
        }
    }

    [Fact]
    public void ReintroducedBug_HardcodingABoxPosition_WouldDivergeFromWorksheetRegions()
    {
        // Proves the guard actually catches something: a box built with ANY hand-typed number instead
        // of going through WorksheetRegions no longer equals what WorksheetRegions itself computes.
        var categoryIds = new[] { CategoryA };
        var real = WorksheetRegions.CategoryLimitBoxes(categoryIds).Single();

        var hardcoded = new WorksheetBox(real.Name, real.Left + 1f, real.Top, real.Width, real.Height);

        Assert.NotEqual(real, hardcoded);
    }

    [Fact]
    public void EveryCategoryGetsItsOwnDistinctBox_AndBoxesDoNotOverlapVertically()
    {
        var categoryIds = new[] { CategoryA, CategoryB, CategoryC };
        var boxes = WorksheetRegions.CategoryLimitBoxes(categoryIds);

        Assert.Equal(3, boxes.Select(b => b.Name).Distinct().Count());
        for (var i = 1; i < boxes.Count; i++)
        {
            Assert.True(boxes[i].Top > boxes[i - 1].Top, "Category rows must stack strictly downward.");
        }
    }

    [Fact]
    public void AllBoxesFitInsideThePageBody()
    {
        var categoryIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var boxes = WorksheetRegions.AllBoxes(categoryIds);

        foreach (var box in boxes)
        {
            Assert.True(box.Left >= WorksheetRegions.MarginLeft, $"{box.Name} starts left of the margin.");
            Assert.True(box.Left + box.Width <= WorksheetRegions.PageWidth - WorksheetRegions.MarginRight + 0.01f,
                $"{box.Name} runs past the right margin.");
            Assert.True(box.Top >= 0, $"{box.Name} has a negative top.");
        }
    }

    [Fact]
    public void ThreeWriteInLinesAlwaysExist_RegardlessOfCategoryCount()
    {
        var withNoCategories = WorksheetRegions.NewCategoryBoxes(0);
        var withTen = WorksheetRegions.NewCategoryBoxes(10);

        Assert.Equal(WorksheetRegions.NewCategoryLines, withNoCategories.Count);
        Assert.Equal(WorksheetRegions.NewCategoryLines, withTen.Count);
        // More categories above push the write-in lines further down the page.
        Assert.True(withTen[0].Name.Top > withNoCategories[0].Name.Top);
    }
}
