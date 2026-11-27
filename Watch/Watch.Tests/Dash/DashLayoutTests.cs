using Watch.Domain.Dash;

namespace Watch.Tests.Dash;

/// <summary>
/// Dash's screens as the mockups draw them (mock: People waiting, A conversation, Replying by voice), in fractions of
/// the round screen's radius: every row, bubble, pill and word the mockups place sits inside the circle.
/// </summary>
public class DashLayoutTests
{
    // Whether a point given in fractions of the radius (0 to 2 across) is on the round screen.
    private static bool Inside(double x, double y) => Math.Sqrt((x - 1) * (x - 1) + (y - 1) * (y - 1)) <= 1;

    private static void BoxInside(double left, double top, double width, double height)
    {
        Assert.True(Inside(left, top) && Inside(left + width, top) && Inside(left, top + height) && Inside(left + width, top + height),
            $"A box at ({left}, {top}) of {width} by {height} runs off the round screen.");
    }

    [Fact]
    public void TheMockupsRowsSitInsideTheCircle()
    {
        for (var row = 0; row < 3; row++)
        {
            var centre = DashLayout.FirstRow + row * DashLayout.RowPitch;
            BoxInside(DashLayout.RowLeft, centre - DashLayout.RowHeight / 2, DashLayout.RowWidth, DashLayout.RowHeight);
        }
    }

    [Fact]
    public void ARowIsCentred() => Assert.Equal(2, DashLayout.RowLeft * 2 + DashLayout.RowWidth, 9);

    [Fact]
    public void RowsDoNotTouch() => Assert.True(DashLayout.RowPitch > DashLayout.RowHeight);

    [Fact]
    public void TheFirstBubblesSitInsideTheCircle()
    {
        BoxInside(DashLayout.BubbleSide, DashLayout.BubblesTop, DashLayout.BubbleWidth, DashLayout.BubbleMinHeight);
        BoxInside(2 - DashLayout.BubbleSide - DashLayout.BubbleWidth, DashLayout.BubblesTop, DashLayout.BubbleWidth, DashLayout.BubbleMinHeight);
    }

    [Fact]
    public void ThePillsSitInsideTheCircle()
    {
        foreach (var (width, centre) in new[]
        {
            (DashLayout.ReplyWidth, DashLayout.ReplyCentre), (DashLayout.ReplyToAllWidth, DashLayout.ReplyCentre), (DashLayout.SendWidth, DashLayout.SendCentre),
        })
        {
            BoxInside(1 - width / 2, centre - DashLayout.PillHeight / 2, width, DashLayout.PillHeight);
        }
    }

    [Fact]
    public void TheQuieterChoicesSitInsideTheCircle()
    {
        Assert.True(Inside(1 - DashLayout.SecondaryOffset, DashLayout.SecondaryCentre));
        Assert.True(Inside(1 + DashLayout.SecondaryOffset, DashLayout.SecondaryCentre));
    }

    [Fact]
    public void TheSendPillIsBelowTheWordsAndAboveTheChoices()
    {
        Assert.True(DashLayout.SendCentre - DashLayout.PillHeight / 2 > DashLayout.WordsTop);
        Assert.True(DashLayout.SecondaryCentre > DashLayout.SendCentre + DashLayout.PillHeight / 2);
    }

    [Fact]
    public void TheHeaderIsAboveTheRows()
    {
        Assert.True(DashLayout.TimeCentre < DashLayout.GlyphTop);
        Assert.True(DashLayout.GlyphTop + DashLayout.GlyphSide < DashLayout.FirstRow - DashLayout.RowHeight / 2);
    }
}
