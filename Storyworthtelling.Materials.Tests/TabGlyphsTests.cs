namespace Storyworthtelling.Materials.Tests;

/// <summary>The one set of tab glyphs (DESIGN.md §10, the sheet approved in W8.8).</summary>
public class TabGlyphsTests
{
    private static readonly string[] Approved =
    [
        "Settings", "Calendar", "Figure", "Bowl", "Page", "Dumbbell", "Camera", "HangingPrint", "ScanFrame", "Folder",
        "Mailbox", "Rolodex", "Note", "Microphone", "PlayCircle", "Compass", "Metronome", "CardStack", "List",
        "Baseball", "Television", "Bank", "Staircase", "Ledger", "MoneyBag", "OpenBook", "BookShelf", "Sunrise", "Checklist",
    ];

    [Fact]
    public void TheSetIsTheApprovedSheet() => Assert.Equal(Approved.Order(), TabGlyphs.All.Keys.Order());

    [Fact]
    public void NoTwoGlyphsAreTheSameDrawing() =>
        Assert.Equal(TabGlyphs.All.Count, TabGlyphs.All.Values.Distinct().Count());

    [Fact]
    public void EveryGlyphParsesAndStaysInsideItsGridWithRoomForTheStroke()
    {
        foreach (var (name, data) in TabGlyphs.All)
        {
            var path = PathData.Parse(data);
            Assert.True(path.Count > 1, name);
            foreach (var command in path.Where(c => c.Verb != PathVerb.Close))
            {
                // A 2-unit stroke reaches 1 unit past the line, and the grid is 24.
                Assert.InRange(command.X, 1.5f, 22.5f);
                Assert.InRange(command.Y, 1.5f, 22.5f);
            }
        }
    }

    [Fact]
    public void TheStrokeIsTwoUnitsOnATwentyFourGrid()
    {
        Assert.Equal(24f, TabGlyphs.Viewport);
        Assert.Equal(2f, TabGlyphs.StrokeWidth);
    }
}
