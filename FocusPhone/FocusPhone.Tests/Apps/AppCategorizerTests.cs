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
