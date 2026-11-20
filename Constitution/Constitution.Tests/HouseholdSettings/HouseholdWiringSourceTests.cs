using Constitution.Tests.Settings;

namespace Constitution.Tests.HouseholdSettings;

public class HouseholdWiringSourceTests
{
    private static string Source(string relative)
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(candidate => ConstitutionSource.Relative(candidate) == relative);
        Assert.True(file is not null, relative + " wasn’t found.");
        return File.ReadAllText(file!);
    }

    [Fact]
    public void SettingsAreaDrawsTheHouseholdGroup()
    {
        var area = Source("Constitution.Android/SettingsArea.cs");

        Assert.Contains("\"Household\"", area);
        Assert.Contains("HouseholdGroupView", area);
    }

    [Fact]
    public void HouseholdGroupFollowsEquipmentAmongTheAppsOwn()
    {
        // The suite's anatomy (audit W8.4b): Connections and the Vault first, Diagnostics last, the app's own between.
        var area = Source("Constitution.Android/SettingsArea.cs");
        var connections = area.IndexOf("new SettingsSection(SettingsAnatomy.Connections", StringComparison.Ordinal);
        var vault = area.IndexOf("new SettingsSection(SettingsAnatomy.Vault", StringComparison.Ordinal);
        var equipment = area.IndexOf("new SettingsSection(EquipmentGroup", StringComparison.Ordinal);
        var household = area.IndexOf("new SettingsSection(HouseholdGroup", StringComparison.Ordinal);
        var diagnostics = area.IndexOf("new SettingsSection(SettingsAnatomy.Diagnostics", StringComparison.Ordinal);

        Assert.True(connections >= 0 && connections < vault && vault < equipment && equipment < household && household < diagnostics);
    }

    [Fact]
    public void TheGroupSavesThroughTheModelAndTheFile()
    {
        var view = Source("Constitution.Android/Settings/HouseholdGroupView.cs");
        var store = Source("Constitution.Android/Settings/HouseholdSetupStore.cs");

        Assert.Contains("store.Save", view);
        Assert.Contains("PlanningSetupFromRoster.ForSave", store);
        Assert.Contains("PlanningSetupFile.Write", store);
        Assert.DoesNotContain("Toast", view + store);
        Assert.DoesNotContain("AlertDialog", view + store);
    }

    [Fact]
    public void TheStoreSavesForTheHouseholdFilesRoster()
    {
        // The roster is the household's list of adults; a store that saved only for the setup file's own adults refused every choice on
        // a phone whose setup file named nobody, and the page redrew blank.
        var area = Source("Constitution.Android/SettingsArea.cs");

        Assert.Contains("new HouseholdSetupStore(host, () => ConstitutionHousehold.For(host).Service.Roster)", area);
    }

    [Fact]
    public void TheGroupNeverReadsABirthdate()
    {
        Assert.DoesNotContain("Birthdate", Source("Constitution.Android/Settings/HouseholdGroupView.cs"));
    }

    [Fact]
    public void TheSetupFileLivesInTheWeeklyRunsFolder()
    {
        var store = Source("Constitution.Android/Settings/HouseholdSetupStore.cs");

        Assert.Contains("WorkoutWeekPaths.Folder", store);
        Assert.Contains("WorkoutWeekCalendarSeams.SetupFile", store);
    }
}
