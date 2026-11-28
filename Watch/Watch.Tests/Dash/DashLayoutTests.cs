using Watch.Domain.Dash;

namespace Watch.Tests.Dash;

/// <summary>
/// Dash's screens as the mockups draw them (mock: People waiting, A conversation, Replying by voice, A group text with a
/// photo, Filed with Undo, After the call, Mail; and the choice before an act, on After the call's layout), in fractions of the round screen's
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
    public void MailSitsWhereTheMockupDrawsIt()
    {
        // mock: People waiting draws "Mail" a row's pitch under its third row, at 1.7 of the radius: a row's height of
        // entry, the gap under the third row, is centred there (B7; New text, which B6 put there, follows it).
        var thirdRowFoot = DashLayout.FirstRow + 2 * DashLayout.RowPitch + DashLayout.RowHeight / 2;
        var centre = thirdRowFoot + DashLayout.EntryGap + DashLayout.RowHeight / 2;

        Assert.Equal(1.7, centre, 9);
        Assert.True(Inside(1, centre));
        Assert.True(DashLayout.EntryGap > 0);
    }

    // ---- B7: After the call and Mail, as the mockups draw them ----

    [Fact]
    public void AfterTheCallIsWhereTheMockupDrawsIt()
    {
        // mock: After the call: the head, "Call with Robin" at 0.66, "6 min, just now" at 0.84, Add a note (1.0 wide) at
        // 1.18 and Not now at 1.48.
        Assert.Equal((0.66, 0.84, 1.18, 1.0, 1.48),
            (DashLayout.AfterCallTitleCentre, DashLayout.AfterCallLineCentre, DashLayout.AfterCallPillCentre, DashLayout.AfterCallPillWidth,
                DashLayout.AfterCallChoiceCentre));
        Assert.True(DashLayout.GlyphTop + DashLayout.GlyphSide < DashLayout.AfterCallTitleCentre);
        Assert.True(DashLayout.AfterCallTitleCentre < DashLayout.AfterCallLineCentre);
        Assert.True(DashLayout.AfterCallLineCentre < DashLayout.AfterCallPillCentre - DashLayout.PillHeight / 2);
        Assert.True(DashLayout.AfterCallChoiceCentre > DashLayout.AfterCallPillCentre + DashLayout.PillHeight / 2);
        BoxInside(1 - DashLayout.AfterCallPillWidth / 2, DashLayout.AfterCallPillCentre - DashLayout.PillHeight / 2, DashLayout.AfterCallPillWidth, DashLayout.PillHeight);
        Assert.True(Inside(1, DashLayout.AfterCallChoiceCentre));
    }

    [Fact]
    public void TheChoiceBeforeAnActIsAfterTheCallsLayout()
    {
        // B5 laid the choice before an act out as After the call is drawn; it is now named from it.
        Assert.Equal(
            (DashLayout.AfterCallTitleCentre, DashLayout.AfterCallPillCentre, DashLayout.AfterCallPillWidth, DashLayout.AfterCallChoiceCentre),
            (DashLayout.ActTitleCentre, DashLayout.ActPillCentre, DashLayout.ActPillWidth, DashLayout.ActChoiceCentre));
    }

    [Fact]
    public void MailLinesAreWhereTheMockupDrawsThem()
    {
        // mock: Mail: its name at the top as a conversation's, then each line a subject over who and when, centred at 0.6
        // and every 0.36 after, the subject fitted to 1.4 of the radius.
        Assert.Equal((0.6, 0.36, 1.4, 0.05, 0.1),
            (DashLayout.MailFirst, DashLayout.MailPitch, DashLayout.MailWidth, DashLayout.MailSubjectRise, DashLayout.MailSenderDrop));
        Assert.True(DashLayout.TitleCentre < DashLayout.MailFirst - DashLayout.MailPitch / 2);
        for (var line = 0; line < 3; line++)
        {
            var centre = DashLayout.MailFirst + line * DashLayout.MailPitch;
            BoxInside(1 - DashLayout.MailWidth / 2, centre - DashLayout.MailSubjectRise, DashLayout.MailWidth, DashLayout.MailSubjectRise + DashLayout.MailSenderDrop);
        }
    }

    // ---- B8: the People waiting tile (mock: Tile: People waiting, Tile: nobody waiting) ----

    [Fact]
    public void TheTileIsWhereTheMockupDrawsIt()
    {
        Assert.Equal(0.62, DashLayout.TileGlyphSide);
        Assert.Equal(0.66, DashLayout.TileGlyphCentre);
        Assert.Equal(1.0, DashLayout.TileNobodyCentre);
        Assert.Equal(1.12, DashLayout.TileNewestCentre);
        Assert.Equal(1.34, DashLayout.TileEarlierCentre);
        Assert.Equal(1.52, DashLayout.TileEarliestCentre);
        Assert.Equal(1.4, DashLayout.TileNameWidth);
        Assert.Equal(DashLayout.TileEarlierCentre, DashLayout.TileQuietCentre);
    }

    [Theory]
    [InlineData(TileRank.Newest, 1.12)]
    [InlineData(TileRank.Earlier, 1.34)]
    [InlineData(TileRank.Earliest, 1.52)]
    public void EachNameHasItsPlace(TileRank rank, double centre) => Assert.Equal(centre, DashLayout.TileNameCentre(rank));

    [Fact]
    public void TheTilesNamesSitInsideTheCircle()
    {
        foreach (var rank in Enum.GetValues<TileRank>())
        {
            var centre = DashLayout.TileNameCentre(rank);
            Assert.True(Inside(1 - DashLayout.TileNameWidth / 2, centre) && Inside(1 + DashLayout.TileNameWidth / 2, centre),
                $"The {rank} name's line runs off the round screen.");
        }
    }

    [Fact]
    public void TheTilesDashSitsAboveItsNames()
    {
        BoxInside(1 - DashLayout.TileGlyphSide / 2, DashLayout.TileGlyphCentre - DashLayout.TileGlyphSide / 2, DashLayout.TileGlyphSide, DashLayout.TileGlyphSide);
        // The dash's stroke is a fraction of its side; its foot is above the newest name.
        Assert.True(DashLayout.TileGlyphCentre + DashLayout.TileGlyphSide * Watch.Domain.Kit.GlyphGeometry.DashStroke < DashLayout.TileNewestCentre);
        Assert.True(DashLayout.TileNewestCentre < DashLayout.TileEarlierCentre && DashLayout.TileEarlierCentre < DashLayout.TileEarliestCentre);
    }

    [Fact]
    public void NobodysDashIsAtTheCentre() =>
        BoxInside(1 - DashLayout.TileGlyphSide / 2, DashLayout.TileNobodyCentre - DashLayout.TileGlyphSide / 2, DashLayout.TileGlyphSide, DashLayout.TileGlyphSide);
}
