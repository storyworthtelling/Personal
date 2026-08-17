using Storyworthtelling.Paper;

namespace Storyworthtelling.Paper.Tests;

public class EmbeddedTypefacesTests
{
    [Fact]
    public void AllFamiliesLoadWithExpectedNames()
    {
        Assert.Equal("Source Serif 4", EmbeddedTypefaces.SerifRegular.FamilyName);
        Assert.Equal("Source Serif 4", EmbeddedTypefaces.SerifItalic.FamilyName);
        Assert.StartsWith("Source Serif 4", EmbeddedTypefaces.SerifSemibold.FamilyName);
        Assert.StartsWith("Source Serif 4", EmbeddedTypefaces.SerifBlack.FamilyName);
        Assert.Equal("Source Sans 3", EmbeddedTypefaces.SansRegular.FamilyName);
        Assert.StartsWith("Source Sans 3", EmbeddedTypefaces.SansSemibold.FamilyName);
    }

    [Fact]
    public void BlackDisplayWeightIsHeavy()
    {
        // The reworked P4 Display weight — must actually be the Black cut, not a re-labeled Regular.
        Assert.True(EmbeddedTypefaces.SerifBlack.FontWeight >= 700,
            $"Serif Black weight is {EmbeddedTypefaces.SerifBlack.FontWeight}.");
    }

    [Fact]
    public void ItalicFaceIsItalic()
    {
        Assert.True(EmbeddedTypefaces.SerifItalic.IsItalic);
        Assert.False(EmbeddedTypefaces.SerifRegular.IsItalic);
    }
}
