using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;
using Constitution.Tests.Scraps;

namespace Constitution.Tests.Scrapbook;

/// <summary>
/// C-3.2.3: where a scrap's paper and content sit inside the box <see cref="ScrapLayout"/>
/// decided. The layout adds padding and border to the measured content; this says which side of
/// the box each of them is on, so the view cannot put the content a pixel off the paper.
/// </summary>
public class ScrapBoxTests
{
    public static IEnumerable<object[]> Kinds() => ScrapFixtures.AllKinds.Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BorderAddsUpToTheStylesVerticalBorder(ScrapKind kind)
    {
        var border = ScrapBox.Border(kind);

        Assert.Equal(ScrapStyle.For(kind).BorderVerticalDp, border.Top + border.Bottom);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ContentIsBorderPlusPadding(ScrapKind kind)
    {
        var style = ScrapStyle.For(kind);
        var border = ScrapBox.Border(kind);
        var content = ScrapBox.Content(kind);

        Assert.Equal(border.Left + style.PaddingStartDp, content.Left);
        Assert.Equal(border.Top + style.PaddingTopDp, content.Top);
        Assert.Equal(border.Right + style.PaddingEndDp, content.Right);
        Assert.Equal(border.Bottom + style.PaddingBottomDp, content.Bottom);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ABoxIsExactlyItsContentPlusTheInsets(ScrapKind kind)
    {
        var placed = ScrapLayout.Place([new Scrap(kind, "only", 87)], ScrapFixtures.WellWidthFor(393))[0];
        var content = ScrapBox.Content(kind);

        Assert.Equal(87 + content.Top + content.Bottom, placed.Height, 6);
    }

    [Fact]
    public void BordersAreWhereTheCssHasThem()
    {
        // .scrap border 1px all round; mag and chart none; note has no top border.
        Assert.Equal(new BoxInsets(1, 1, 1, 1), ScrapBox.Border(ScrapKind.IndexCard));
        Assert.Equal(new BoxInsets(1, 1, 1, 1), ScrapBox.Border(ScrapKind.Receipt));
        Assert.Equal(new BoxInsets(1, 0, 1, 1), ScrapBox.Border(ScrapKind.NotepadLeaf));
        Assert.Equal(new BoxInsets(0, 0, 0, 0), ScrapBox.Border(ScrapKind.MagazineClipping));
        Assert.Equal(new BoxInsets(0, 0, 0, 0), ScrapBox.Border(ScrapKind.ChartScrap));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ThePaperIsTheBoxLessItsBorder(ScrapKind kind)
    {
        var border = ScrapBox.Border(kind);
        var paper = ScrapBox.Paper(kind, 300, 120);

        Assert.Equal(border.Left, paper.X);
        Assert.Equal(border.Top, paper.Y);
        Assert.Equal(300 - border.Left - border.Right, paper.Width);
        Assert.Equal(120 - border.Top - border.Bottom, paper.Height);
    }
}
