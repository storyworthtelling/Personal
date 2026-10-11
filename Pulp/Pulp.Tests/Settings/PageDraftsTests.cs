using Pulp.Domain.Settings;

namespace Pulp.Tests.Settings;

/// <summary>
/// The Household page rebuilds its views on every change and every sync pass; what someone was half-way
/// through typing must survive that, and a sync pass must not even try to rebuild under their fingers.
/// </summary>
public class PageDraftsTests
{
    [Fact]
    public void ADraftSurvivesUntilItIsCleared()
    {
        var drafts = new PageDrafts();
        drafts.Set("rename:pip-example", "Pippi");

        Assert.Equal("Pippi", drafts.Get("rename:pip-example", "Pip"));

        drafts.Clear("rename:pip-example");
        Assert.Equal("Pip", drafts.Get("rename:pip-example", "Pip"));
    }

    [Fact]
    public void AFieldWithNoDraftShowsItsFallback()
    {
        Assert.Equal("Pip", new PageDrafts().Get("rename:pip-example", "Pip"));
    }

    [Fact]
    public void ADraftIsPerFieldNotPerPage()
    {
        var drafts = new PageDrafts();
        drafts.Set("add", "Wren");
        drafts.Set("search:pip-example", "lantern");

        Assert.Equal("Wren", drafts.Get("add", string.Empty));
        Assert.Equal("lantern", drafts.Get("search:pip-example", string.Empty));
        Assert.Equal(string.Empty, drafts.Get("search:avery-example", string.Empty));
    }

    [Fact]
    public void AnEmptiedFieldIsNoDraft()
    {
        var drafts = new PageDrafts();
        drafts.Set("add", "Wren");
        drafts.Set("add", string.Empty);

        Assert.False(drafts.Any);
    }

    [Fact]
    public void ASyncPassWaitsWhileAFieldHasFocusOrAHalfTypedDraft()
    {
        Assert.True(ReloadPolicy.ShouldWait(ReloadCause.Sync, fieldFocused: true, hasDraft: false));
        Assert.True(ReloadPolicy.ShouldWait(ReloadCause.Sync, fieldFocused: false, hasDraft: true));
        Assert.False(ReloadPolicy.ShouldWait(ReloadCause.Sync, fieldFocused: false, hasDraft: false));
    }

    [Fact]
    public void AReloadAfterOwnWritesNeverWaitsBecauseTheDraftsAreKept()
    {
        Assert.False(ReloadPolicy.ShouldWait(ReloadCause.OwnWrites, fieldFocused: true, hasDraft: true));
        Assert.False(ReloadPolicy.ShouldWait(ReloadCause.Opened, fieldFocused: true, hasDraft: true));
    }
}
