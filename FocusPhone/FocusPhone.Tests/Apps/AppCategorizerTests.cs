using FocusPhone.Domain.Apps;
using Xunit;

namespace FocusPhone.Tests.Apps;

public class AppCategorizerTests
{
    private readonly AppCategorizer categorizer = new();

    private static AppInfo CleanApp(string packageName = "com.example.notes") =>
        new(packageName, HandlesHttpViewIntents: false, IsCategoryGame: false);

    [Fact]
    public void CleanAppWithEveryoneRating_IsAllowed()
    {
        Assert.Equal(AppVerdict.Allowed, categorizer.Categorize(CleanApp(), ContentRating.Everyone));
    }

    [Fact]
    public void CleanAppWithTeenRating_IsAllowed()
    {
        Assert.Equal(AppVerdict.Allowed, categorizer.Categorize(CleanApp(), ContentRating.Teen));
    }

    [Fact]
    public void BrowserApp_IsBlocked()
    {
        var browser = new AppInfo("org.mozilla.firefox", HandlesHttpViewIntents: true, IsCategoryGame: false);

        Assert.Equal(AppVerdict.Blocked, categorizer.Categorize(browser, ContentRating.Everyone));
    }

    [Fact]
    public void GameApp_IsBlocked()
    {
        var game = new AppInfo("com.example.game", HandlesHttpViewIntents: false, IsCategoryGame: true);

        Assert.Equal(AppVerdict.Blocked, categorizer.Categorize(game, ContentRating.Everyone));
    }

    [Fact]
    public void MatureRating_IsQuarantined()
    {
        Assert.Equal(AppVerdict.Quarantined, categorizer.Categorize(CleanApp(), ContentRating.Mature));
    }

    [Fact]
    public void UnknownRating_IsQuarantined()
    {
        Assert.Equal(AppVerdict.Quarantined, categorizer.Categorize(CleanApp(), ContentRating.Unknown));
    }

    [Fact]
    public void BlockedPackage_StaysBlockedOnReinstall_EvenIfOtherwiseClean()
    {
        categorizer.Block("com.example.notes");

        Assert.Equal(AppVerdict.Blocked, categorizer.Categorize(CleanApp(), ContentRating.Everyone));
    }

    [Fact]
    public void AdminAllowedPackage_IsAllowed_DespiteUnknownRating()
    {
        categorizer.Allow("com.example.notes");

        Assert.Equal(AppVerdict.Allowed, categorizer.Categorize(CleanApp(), ContentRating.Unknown));
    }

    [Fact]
    public void Blocklist_TakesPrecedenceOverAllowlist()
    {
        categorizer.Allow("com.example.notes");
        categorizer.Block("com.example.notes");

        Assert.Equal(AppVerdict.Blocked, categorizer.Categorize(CleanApp(), ContentRating.Everyone));
    }

    [Fact]
    public void Block_RemovesPackageFromAllowlist()
    {
        categorizer.Allow("com.example.notes");
        categorizer.Block("com.example.notes");
        var restored = new AppCategorizer(categorizer.Export());

        Assert.Equal(AppVerdict.Blocked, restored.Categorize(CleanApp(), ContentRating.Everyone));
    }

    [Fact]
    public void Unblock_AllowsPreviouslyBlockedPackage()
    {
        categorizer.Block("com.example.notes");

        categorizer.Unblock("com.example.notes");

        Assert.Equal(AppVerdict.Allowed, categorizer.Categorize(CleanApp(), ContentRating.Unknown));
        Assert.DoesNotContain("com.example.notes", categorizer.Export().BlockedPackages);
    }

    [Fact]
    public void OwnerPrefixApp_IsAllowed_DespiteUnknownRating()
    {
        // Sideloaded first-party apps (Storyboarder, Ledger, …) are never on the
        // Play Store, so their rating always resolves Unknown; the owner prefix
        // clears them so a reinstall can't re-quarantine them.
        var storyboarder = CleanApp("com.storyworthtelling.storyboarder");

        Assert.Equal(AppVerdict.Allowed, categorizer.Categorize(storyboarder, ContentRating.Unknown));
    }

    [Fact]
    public void OwnerPrefixApp_IsAllowed_EvenIfHandlesWebIntents()
    {
        var ownApp = new AppInfo(
            "com.storyworthtelling.ledger", HandlesHttpViewIntents: true, IsCategoryGame: false);

        Assert.Equal(AppVerdict.Allowed, categorizer.Categorize(ownApp, ContentRating.Unknown));
    }

    [Fact]
    public void OwnerPrefixApp_CanStillBeExplicitlyBlocked()
    {
        categorizer.Block("com.storyworthtelling.storyboarder");

        Assert.Equal(AppVerdict.Blocked,
            categorizer.Categorize(CleanApp("com.storyworthtelling.storyboarder"), ContentRating.Everyone));
    }

    [Fact]
    public void LookalikePackageOutsideOwnerPrefix_IsStillQuarantined()
    {
        // The prefix ends in a dot, so a namespace that merely starts with the
        // same letters is not treated as first-party.
        var lookalike = CleanApp("com.storyworthtellingfake.app");

        Assert.Equal(AppVerdict.Quarantined, categorizer.Categorize(lookalike, ContentRating.Unknown));
    }

    [Fact]
    public void ExportedLists_RestoreInNewInstance()
    {
        categorizer.Block("com.blocked.app");
        categorizer.Allow("com.allowed.app");

        var restored = new AppCategorizer(categorizer.Export());

        Assert.Equal(AppVerdict.Blocked,
            restored.Categorize(CleanApp("com.blocked.app"), ContentRating.Everyone));
        Assert.Equal(AppVerdict.Allowed,
            restored.Categorize(CleanApp("com.allowed.app"), ContentRating.Unknown));
    }
}
