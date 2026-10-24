using Constitution.Tests.Settings;

namespace Constitution.Tests.HouseholdSettings;

public class HouseholdWiringSourceTests
{
    private static string Source(string relative)
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(candidate => ConstitutionSource.Relative(candidate) == relative);
        Assert.True(file is not null, relative + " was not found.");
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
    public void HouseholdGroupIsBetweenEquipmentAndConnections()
    {
        var area = Source("Constitution.Android/SettingsArea.cs");

        Assert.True(area.IndexOf("EquipmentGroup, \"Equipment\"") < area.IndexOf("HouseholdGroup, \"Household\""));
        Assert.True(area.IndexOf("HouseholdGroup, \"Household\"") < area.IndexOf("ConnectionsGroup, \"Connections\""));
    }

    [Fact]
    public void TheGroupSavesThroughTheModelAndTheFile()
    {
        var view = Source("Constitution.Android/Settings/HouseholdGroupView.cs");
        var store = Source("Constitution.Android/Settings/HouseholdSetupStore.cs");

        Assert.Contains("store.Save", view);
        Assert.Contains("PlanningSetupFromRoster.Apply", store);
        Assert.Contains("PlanningSetupFile.Write", store);
        Assert.DoesNotContain("Toast", view + store);
        Assert.DoesNotContain("AlertDialog", view + store);
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
