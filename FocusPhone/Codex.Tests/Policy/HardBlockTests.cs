using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

public class HardBlockTests
{
    private const int Game = 0;
    private const int Social = 4;
    private const int Undefined = -1;
    private const int Productivity = 7;

    [Theory]
    [InlineData(Game, true)]
    // CATEGORY_SOCIAL is NOT auto-blocked: first-party utilities (Google Messages, Contacts, Meet,
    // Gemini) declare appCategory="social" and were wrongly hard-suspended. Real social media is
    // caught by the curated list instead.
    [InlineData(Social, false)]
    [InlineData(Undefined, false)]
    [InlineData(Productivity, false)]
    public void IsNeverVisibleCategory_FlagsGamesOnly(int category, bool expected)
    {
        Assert.Equal(expected, HardBlock.IsNeverVisibleCategory(category));
    }

    [Fact]
    public void Detect_CatchesGamesByAndroidCategory()
    {
        var blocked = HardBlock.Detect([("com.unknown.gamething", Game)]);

        Assert.Contains("com.unknown.gamething", blocked);
    }

    [Fact]
    public void Detect_DoesNotBlockSocialCategoryUtilities()
    {
        // Google Messages / Contacts / Meet declare the social category but must stay visible.
        var blocked = HardBlock.Detect(
        [
            ("com.google.android.apps.messaging", Social),
            ("com.google.android.contacts", Social),
            ("com.google.android.apps.tachyon", Social),
        ]);

        Assert.Empty(blocked);
    }

    [Fact]
    public void Detect_CatchesCuratedPackages_EvenWhenTheyMisdeclareTheirCategory()
    {
        // Dating/short-form/etc. often report a benign category — the curated list still blocks them.
        var blocked = HardBlock.Detect(
        [
            ("com.tinder", Undefined),
            ("com.zhiliaoapp.musically", Productivity),
            ("com.instagram.android", Undefined),
        ]);

        Assert.Contains("com.tinder", blocked);
        Assert.Contains("com.zhiliaoapp.musically", blocked);
        Assert.Contains("com.instagram.android", blocked);
    }

    [Fact]
    public void Detect_LeavesOrdinaryAppsAlone()
    {
        var blocked = HardBlock.Detect([("com.storyworthtelling.focusphone.audio", Undefined), ("com.google.android.apps.maps", 6)]);

        Assert.Empty(blocked);
    }
}
