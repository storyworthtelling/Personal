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
    [InlineData(Social, false)]
    [InlineData(Undefined, false)]
    [InlineData(Productivity, false)]
    public void IsNeverVisibleCategory_FlagsGamesOnly(int category, bool expected)
    {
        Assert.Equal(expected, HardBlock.IsNeverVisibleCategory(category));
    }

    [Theory]
    [InlineData(Social, true)]
    [InlineData(Game, false)]
    [InlineData(Undefined, false)]
    public void IsSocialCategory_FlagsSocialOnly(int category, bool expected)
    {
        Assert.Equal(expected, HardBlock.IsSocialCategory(category));
    }

    [Theory]
    [InlineData("com.google.android.apps.messaging", true)]
    [InlineData("com.google.android.apps.bard", true)]
    [InlineData("com.google.android.apps.tachyon", true)]
    [InlineData("com.google.android.contacts", true)]
    [InlineData("com.google.android.youtube", true)]
    [InlineData("com.storyworthtelling.focusphone.audio", true)]
    [InlineData("com.facebook.orca", false)]
    [InlineData("com.whatsapp", false)]
    [InlineData("com.google.ar.lens", true)]
    [InlineData("com.google.android.apps.docs", true)]
    public void IsFirstParty_IsGoogleOrSuiteNamespaceOnly(string package, bool expected)
    {
        Assert.Equal(expected, HardBlock.IsFirstParty(package));
    }

    [Fact]
    public void Detect_CatchesGamesByAndroidCategory()
    {
        var blocked = HardBlock.Detect([("com.unknown.gamething", Game)]);

        Assert.Contains("com.unknown.gamething", blocked);
    }

    [Fact]
    public void Detect_BlocksSocialCategoryApps_ThatAreNotFirstParty()
    {
        var blocked = HardBlock.Detect(
        [
            ("com.whatsapp", Social),
            ("org.telegram.messenger", Social),
        ]);

        Assert.Contains("com.whatsapp", blocked);
        Assert.Contains("org.telegram.messenger", blocked);
    }

    [Fact]
    public void Detect_LeavesFirstPartyGoogleSocialAppsVisible()
    {
        // Google Messages / Contacts / Meet / Gemini declare the social category but are first-party.
        var blocked = HardBlock.Detect(
        [
            ("com.google.android.apps.messaging", Social),
            ("com.google.android.contacts", Social),
            ("com.google.android.apps.tachyon", Social),
            ("com.google.android.apps.bard", Social),
        ]);

        Assert.Empty(blocked);
    }

    [Fact]
    public void Detect_LeavesOwnSuiteSocialAppsVisible()
    {
        var blocked = HardBlock.Detect([("com.storyworthtelling.someapp", Social)]);

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

    [Fact]
    public void Detect_BlocksAppsRatedMatureOrAdultsOnly()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var ratings = Codex.Domain.Apps.RatingBook.Empty
            .Record("com.example.forum", 1, Rated(Codex.Domain.Apps.ContentRating.Mature), now)
            .Record("com.example.after", 1, Rated(Codex.Domain.Apps.ContentRating.AdultsOnly), now)
            .Record("com.example.notes", 1, Rated(Codex.Domain.Apps.ContentRating.Teen), now);

        var blocked = HardBlock.Detect(
            [("com.example.forum", Undefined), ("com.example.after", Productivity), ("com.example.notes", Productivity),
             ("com.example.unlooked", Productivity)],
            ratings);

        Assert.Equal(new HashSet<string> { "com.example.forum", "com.example.after" }, blocked);
    }

    [Fact]
    public void Detect_NeverBlocksTheOwnersSuiteForItsRating()
    {
        var ratings = Codex.Domain.Apps.RatingBook.Empty.Record("com.storyworthtelling.pulp", 1,
            Rated(Codex.Domain.Apps.ContentRating.Mature), DateTimeOffset.UnixEpoch);

        Assert.Empty(HardBlock.Detect([("com.storyworthtelling.pulp", Undefined)], ratings));
    }

    private static Codex.Domain.Apps.PlayLookup Rated(Codex.Domain.Apps.ContentRating rating) =>
        new(Codex.Domain.Apps.RatingLookupOutcome.Rated, rating);

    [Fact]
    public void Detect_NeverBlocksPulpsStreamingServicesForTheirRating()
    {
        // Pulp hands the viewer to these apps; a rating change must not take its hand-off away.
        var ratings = Codex.Domain.Apps.RatingBook.Empty;
        foreach (var package in Codex.Domain.Apps.LockedOpenCatalogue.Packages)
        {
            ratings = ratings.Record(package, 1, Rated(Codex.Domain.Apps.ContentRating.Mature), DateTimeOffset.UnixEpoch);
        }

        Assert.Empty(HardBlock.Detect(
            Codex.Domain.Apps.LockedOpenCatalogue.Packages.Select(package => (package, Undefined)), ratings));
    }
}
