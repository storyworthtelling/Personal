namespace Constitution.Tests.PersonSheet;

/// <summary>The head's half of the sheet, read as source: the model decides what is offered, the view only paints it.</summary>
public class PersonSheetSourceTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative));

    // Comments may say what the page never does ("never a toast"); only code counts.
    private static string Code(string relative) =>
        string.Join('\n', Read(relative).Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private const string View = "Constitution.Android/Settings/PersonSheetView.cs";
    private const string Area = "Constitution.Android/SettingsArea.cs";

    [Fact]
    public void TheViewNeverNamesABirthdate()
    {
        var source = Read(View);

        Assert.DoesNotContain("birth", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemovalAsksNoOneToConfirm()
    {
        foreach (var file in new[] { View, Area, "Constitution.Android/Settings/HouseholdGroupView.cs" })
        {
            var source = Code(file);
            foreach (var word in new[] { "AlertDialog", "Confirm", "Toast", "Snackbar", "Progress" + "Bar" })
            {
                Assert.DoesNotContain(word, source);
            }
        }
    }

    [Fact]
    public void TheViewPaintsOnlyTheActionsTheModelOffers()
    {
        var source = Read(View);

        Assert.Contains("model.Actions", source);
        Assert.DoesNotContain("\"Remove\"", source);
        Assert.DoesNotContain("\"Save\"", source);
    }

    [Fact]
    public void TheSheetIsInTheSettingsPinnedBar()
    {
        var area = Read(Area);

        Assert.Contains("BuildBottomBar", area);
        Assert.Contains("PersonSheetView.BuildBar", area);
        Assert.Contains("IUndoableArea", area);
        Assert.Contains("PersonRemovalUndo", area);
    }

    [Fact]
    public void ThePageWritesOnlyThroughTheRosterChanges()
    {
        var area = Read(Area);

        Assert.Contains("HouseholdRosterChanges", area);
        Assert.DoesNotContain("RosterEditor", area);
        Assert.DoesNotContain("HouseholdRecords.Remove", area);
        Assert.DoesNotContain("EditAsync", area);
    }

    [Fact]
    public void AListedPersonOpensTheirSheet()
    {
        var group = Read("Constitution.Android/Settings/HouseholdGroupView.cs");

        Assert.Contains("onOpenPerson", group);
    }
}
