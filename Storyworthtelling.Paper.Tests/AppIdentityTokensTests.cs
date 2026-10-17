using Xunit;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// The printed-page identity tokens carry what the documents themselves need, which for
/// Constitution is one muted hue and nothing else coloured.
/// </summary>
public class AppIdentityTokensTests
{
    /// <summary>
    /// Constitution's documents sign with the muted cerulean: the same 230 degree hue as the
    /// ladder at the Document profile's low chroma, so a greyscale rM2 loses nothing — the rule
    /// is a header, never a cue anything depends on seeing.
    /// </summary>
    [Fact]
    public void ConstitutionDocumentHueIsTheMutedCerulean()
    {
        Assert.Equal("#476F83", AppIdentityTokens.ConstitutionDocumentHex);

        var document = Oklch.FromHex(AppIdentityTokens.ConstitutionDocumentHex);
        var mark = Oklch.FromHex(AppIdentityTokens.ConstitutionMarkHex);

        Assert.True(Oklch.HueSeparation(document.HueDegrees, mark.HueDegrees) <= 2.5,
            $"The Document cerulean is {document.HueDegrees:F1}°, off the ladder's {mark.HueDegrees:F1}°.");
        Assert.True(document.C < mark.C,
            $"The Document profile is muted: chroma {document.C:F3} must sit below the screen mark's {mark.C:F3}.");
    }

    /// <summary>The Document profile adds exactly one identity colour, and it is not a ladder tier.</summary>
    [Fact]
    public void TheDocumentCeruleanIsNotAScreenTier()
    {
        var tiers = new[]
        {
            AppIdentityTokens.ConstitutionFillHex, AppIdentityTokens.ConstitutionGroundHex,
            AppIdentityTokens.ConstitutionMarkHex, AppIdentityTokens.ConstitutionTextHex,
            AppIdentityTokens.ConstitutionDeepHex,
        };

        Assert.DoesNotContain(AppIdentityTokens.ConstitutionDocumentHex, tiers);
    }
}
