namespace Constitution.Tests.Guard;

/// <summary>
/// Report 03 §41 (audit W7): the "week not planned" notice and the Body backup's sentences were written and
/// tested and reached nothing on the device. These name the calls the head now makes.
/// </summary>
public class OrphansWiredTests
{
    private static string Head(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void EveryWeeklyRunStatesTheWeekNotPlannedNotice()
    {
        var services = Head("Constitution.Android/WorkoutWeek/WorkoutWeekServices.cs");

        Assert.Contains("WorkoutWeekNotices.Apply(", services, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsShowsWhatTheBodyBackupLastDid()
    {
        Assert.Contains("BodyBackupNote.For(", Head("Constitution.Android/Services/BodyBackups.cs"), StringComparison.Ordinal);
        Assert.Contains("BodyBackups.LastNote", Head("Constitution.Android/SettingsArea.cs"), StringComparison.Ordinal);
    }
}
