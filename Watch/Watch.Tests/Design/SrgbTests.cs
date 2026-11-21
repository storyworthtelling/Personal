using Watch.Domain.Design;

namespace Watch.Tests.Design;

public class SrgbTests
{
    [Fact]
    public void WhiteIsWhite() => Assert.Equal(unchecked((int)0xFFFFFFFF), Srgb.Argb(new Oklch(1, 0, 0)));

    [Fact]
    public void BlackIsBlack() => Assert.Equal(unchecked((int)0xFF000000), Srgb.Argb(new Oklch(0, 0, 0)));

    [Fact]
    public void RedIsRed() => Assert.Equal(unchecked((int)0xFFFF0000), Srgb.Argb(new Oklch(0.627955, 0.257683, 29.2339)));

    [Fact]
    public void AlphaIsTheTopByte() => Assert.Equal(0x80, (Srgb.Argb(new Oklch(1, 0, 0), 0.5) >> 24) & 0xFF);

    [Fact]
    public void OutOfGamutClips()
    {
        var argb = Srgb.Argb(new Oklch(0.9, 0.4, 140));
        Assert.Equal(0xFF, (argb >> 24) & 0xFF);
    }
}
