using Pulp.Domain.Settings;

namespace Pulp.Tests.Settings;

/// <summary>
/// D106's choice is kept on this phone (the stack is this phone's adult's, D23), in a preferences file
/// an upgraded phone has to find again: rename the file or a key and the stack quietly drops back to
/// three and two. The values are typed out so the guard notices them moving.
/// </summary>
public class StackSizePreferenceTests
{
    [Fact]
    public void TheFileAndKeysAreFrozen()
    {
        Assert.Equal("pulp-stack-size", StackSizePreference.FileName);
        Assert.Equal("films", StackSizePreference.FilmsKey);
        Assert.Equal("shows", StackSizePreference.ShowsKey);
    }

    [Fact]
    public void TheStoreUsesTheFrozenNamesAndStaysOnThisPhone()
    {
        var store = PulpSource.Files(".cs").SingleOrDefault(file => PulpSource.Relative(file) == "Pulp.Android/Settings/StackSizeStore.cs");
        Assert.True(store is not null, "Pulp.Android/Settings/StackSizeStore.cs was not found.");

        var source = File.ReadAllText(store!);
        Assert.Contains("GetSharedPreferences(StackSizePreference.FileName", source, StringComparison.Ordinal);
        Assert.Contains("StackSizePreference.FilmsKey", source, StringComparison.Ordinal);
        Assert.Contains("StackSizePreference.ShowsKey", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HouseholdMaps", source, StringComparison.Ordinal);
    }
}
