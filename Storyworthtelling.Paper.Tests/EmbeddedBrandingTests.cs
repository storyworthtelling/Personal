namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// The owner's medallion loads. A renderer that cannot load a branding asset does not fail — it
/// draws a blank where the mark should be, and the page is already printed by the time anyone
/// notices, which is why this is checked here rather than left to the eye.
/// </summary>
public class EmbeddedBrandingTests
{
    [Fact]
    public void TheOwnerMedallionIsEmbeddedAndDecodes()
    {
        var medallion = EmbeddedBranding.OwnerMedallion;

        Assert.NotNull(medallion);
        Assert.True(medallion!.Width > 0 && medallion.Height > 0);
    }

    /// <summary>
    /// Square, because it is circle-masked when drawn. A rectangular source would be masked to an
    /// ellipse or cropped off-centre, and either reads as a mistake rather than a mark.
    /// </summary>
    [Fact]
    public void TheMedallionIsSquare()
    {
        var medallion = EmbeddedBranding.OwnerMedallion!;

        Assert.Equal(medallion.Width, medallion.Height);
    }
}
