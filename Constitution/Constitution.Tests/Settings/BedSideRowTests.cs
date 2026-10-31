using Constitution.Domain.Settings;

namespace Constitution.Tests.Settings;

/// <summary>Review S7: the Bed side row in Settings, Connections: only for someone with a Sleep Number login, and Left, Right or None.</summary>
public class BedSideRowTests
{
    [Fact]
    public void ItIsShownOnlyWhenALoginIsSet()
    {
        Assert.Null(BedSideRow.Build(loginSet: false, stored: "left"));
        Assert.NotNull(BedSideRow.Build(loginSet: true, stored: string.Empty));
    }

    [Fact]
    public void ItOffersLeftRightAndNoneInThatOrder()
    {
        var row = BedSideRow.Build(loginSet: true, stored: string.Empty)!;

        Assert.Equal(["Left", "Right", "None"], row.Labels);
        Assert.Equal("Bed side", row.Name);
    }

    [Theory]
    [InlineData("left", 0)]
    [InlineData("right", 1)]
    [InlineData("none", 2)]
    [InlineData("", 2)]
    [InlineData("middle", 2)]
    public void TheStoredWordSelectsTheSegment(string stored, int selected)
    {
        Assert.Equal(selected, BedSideRow.Build(loginSet: true, stored)!.Selected);
    }

    [Theory]
    [InlineData(0, "left")]
    [InlineData(1, "right")]
    [InlineData(2, "none")]
    public void ASegmentIsStoredAsItsWord(int index, string stored)
    {
        Assert.Equal(stored, BedSideRow.StoredFor(index));
    }

    [Fact]
    public void ASegmentThatIsNotOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BedSideRow.StoredFor(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => BedSideRow.StoredFor(-1));
    }

    [Fact]
    public void TheCaptionSaysWhatTheBedIsFor()
    {
        var caption = BedSideRow.Build(loginSet: true, stored: "left")!.Caption;

        Assert.Contains("nights", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("password", caption, StringComparison.OrdinalIgnoreCase);
    }
}
