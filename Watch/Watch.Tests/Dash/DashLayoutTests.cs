using Watch.Domain.Dash;

namespace Watch.Tests.Dash;

/// <summary>
/// Dash's screens as the mockups draw them (mock: People waiting, A conversation, Replying by voice, A group text with a
/// photo, Filed with Undo; and the choice before an act, on After the call's layout), in fractions of the round screen's
/// radius: every row, bubble, picture, ring, pill and word the mockups place sits inside the circle.
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
            (DashLayout.ReplyWidth, DashLayout.ReplyCentre), (DashLayout.ReplyToAllWidth, DashLayout.ReplyToAllCentre), (DashLayout.SendWidth, DashLayout.SendCentre),
            (DashLayout.ActPillWidth, DashLayout.ActPillCentre),
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

    // ---- B5: a group's photo, the Undo ring, and the choice before an act ----

    [Fact]
    public void TheMockupsPhotoSitsInsideTheCircle()
    {
        Assert.Equal((1.0, 0.6, 0.06, 0.54), (DashLayout.PhotoWidth, DashLayout.PhotoHeight, DashLayout.PhotoCorner, DashLayout.PhotoTop));
        BoxInside(1 - DashLayout.PhotoWidth / 2, DashLayout.PhotoTop, DashLayout.PhotoWidth, DashLayout.PhotoHeight);
        Assert.True(Inside(1, DashLayout.PhotoTop + DashLayout.PhotoHeight + DashLayout.CaptionCentre));
    }

    [Fact]
    public void AGroupsReplyToAllIsWhereTheMockupDrawsIt()
    {
        // mock: A group text with a photo draws Reply to all higher than A conversation draws Reply, under the caption.
        Assert.Equal(1.52, DashLayout.ReplyToAllCentre, 9);
        Assert.True(DashLayout.ReplyToAllCentre - DashLayout.PillHeight / 2 > DashLayout.PhotoTop + DashLayout.PhotoHeight + DashLayout.CaptionCentre);
    }

    [Fact]
    public void TheUndoRingSitsInsideTheCircleAboveItsWords()
    {
        var ring = Watch.Domain.Kit.GlyphGeometry.UndoGlyph * Watch.Domain.Kit.GlyphGeometry.UndoRingRadius;

        Assert.Equal((0.62, 1.18, 1.4), (DashLayout.UndoGlyphCentre, DashLayout.UndoTitleCentre, DashLayout.UndoPromptCentre));
        Assert.True(Inside(1, DashLayout.UndoGlyphCentre - ring) && Inside(1 - ring, DashLayout.UndoGlyphCentre) && Inside(1 + ring, DashLayout.UndoGlyphCentre));
        Assert.True(DashLayout.UndoGlyphCentre + ring < DashLayout.UndoTitleCentre);
        Assert.True(DashLayout.UndoTitleCentre < DashLayout.UndoPromptCentre && Inside(1, DashLayout.UndoPromptCentre));
    }

    [Fact]
    public void TheChoiceBeforeAnActIsBelowItsName()
    {
        // After the call's layout: the name, then the pill, then the quieter choice under it.
        Assert.True(DashLayout.GlyphTop + DashLayout.GlyphSide < DashLayout.ActTitleCentre);
        Assert.True(DashLayout.ActTitleCentre < DashLayout.ActPillCentre - DashLayout.PillHeight / 2);
        Assert.True(DashLayout.ActChoiceCentre > DashLayout.ActPillCentre + DashLayout.PillHeight / 2);
        Assert.True(Inside(1, DashLayout.ActChoiceCentre));
    }

    // ---- B6: New text, where the mockup draws People waiting's Mail ----

    [Fact]
    public void NewTextSitsWhereTheMockupDrawsMail()
    {
        // mock: People waiting draws "Mail" a row's pitch under its third row, at 1.7 of the radius.
        Assert.Equal(1.7, DashLayout.EntryCentre(3), 9);
        Assert.Equal(DashLayout.FirstRow, DashLayout.EntryCentre(0), 9);
        Assert.True(Inside(1, DashLayout.EntryCentre(3)));
        Assert.True(DashLayout.EntryCentre(1) - DashLayout.RowHeight / 2 > DashLayout.FirstRow + DashLayout.RowHeight / 2);
    }
}
