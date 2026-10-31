using System.Text.RegularExpressions;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>What joins the saved week to the tab and to Today, said by reading the source (the head is compile-checked, not run here).</summary>
public class TabFeedSourceTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static readonly Regex Literal = new(
        @"""#[0-9A-Fa-f]{6,8}""|Color\.ParseColor\(\s*""|SetTextSize\([^;]*,\s*[0-9]|TextSize\s*=\s*[0-9]",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex EmptyCatch = new(@"catch[^{]*\{\s*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Toast = new(@"Toast\.|MakeText|AlertDialog|Snackbar", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static IEnumerable<string> FeedFiles() =>
    [
        "Constitution.Android/WorkoutsTab/WorkoutsTabServices.cs",
        "Constitution.Android/WorkoutsTab/CalendarWeekRequests.cs",
        "Constitution.Infrastructure/Workouts/ReportWorkoutsSource.cs",
        "Constitution.Infrastructure/Workouts/WeekRecordSnapshot.cs",
        "Constitution.Infrastructure/Workouts/TodayPlanFromRecord.cs",
    ];

    [Fact]
    public void TheWorkoutsAreaIsGivenExactlyTheDecoratedSource()
    {
        var services = Read("Constitution.Android/WorkoutsTab/WorkoutsTabServices.cs");

        Assert.Contains(
            "new OverridingWorkoutsSource(new SettlingWorkoutsSource(real, new LedgerSkipSettler(ReadBackServices.LedgerOf(context))), overrides)",
            services,
            StringComparison.Ordinal);
        Assert.Contains("new FileWorkoutOverrides(Path.Combine(folder, WorkoutWeekFiles.OverridesFile))", services, StringComparison.Ordinal);
        Assert.Contains("new WorkoutCalendarFollow(", services, StringComparison.Ordinal);
        Assert.Contains("WorkoutWeekCalendarSeams.Create(", services, StringComparison.Ordinal);
        Assert.Contains("calendarSide.EventEditor", services, StringComparison.Ordinal);
        Assert.Contains("calendarSide.KeyAssignments", services, StringComparison.Ordinal);
        Assert.Contains("calendarSide.WrittenKeys", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAreaAndTheShellActuallyUseIt()
    {
        Assert.Contains("WorkoutsTabServices.Wire(", Read("Constitution.Android/WorkoutsArea.cs"), StringComparison.Ordinal);
        Assert.Contains("WorkoutsTabServices.TodayOf(", Read("Constitution.Android/TodayArea.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameKeyAssignmentsFileAsTheRunIsRead()
    {
        var services = Read("Constitution.Android/WorkoutsTab/WorkoutsTabServices.cs");

        Assert.Contains("calendarSide.KeyAssignments", services, StringComparison.Ordinal);
        Assert.DoesNotContain("new FileKeyAssignments(", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFeedFilesKeepTheHouseRules()
    {
        foreach (var file in FeedFiles())
        {
            var source = Read(file);
            Assert.False(Literal.IsMatch(source), file + " writes a colour or a type size.");
            Assert.False(EmptyCatch.IsMatch(source), file + " has an empty catch.");
            Assert.False(Toast.IsMatch(source), file + " raises a toast or a dialog.");
            Assert.DoesNotContain("CurrentDomain", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheFilesLiveInThePrivateNoBackupFolder()
    {
        var services = Read("Constitution.Android/WorkoutsTab/WorkoutsTabServices.cs");

        Assert.Contains("NoBackupFilesDir", services, StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalFilesDir", services, StringComparison.Ordinal);
        Assert.DoesNotContain("GetExternalStorage", services, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPermissionWasAddedForIt()
    {
        foreach (var file in FeedFiles())
        {
            var source = Read(file);
            Assert.DoesNotContain("UsesPermission", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RequestPermissions", source, StringComparison.Ordinal);
        }

        var declared = Regex.Matches(Read("Constitution.Android/ManifestPermissions.cs"), @"UsesPermission\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        // Internet, and the notification permission the notices need (C-9.1.3, D155); this feed added neither.
        Assert.Equal(2, declared.Count);
        Assert.Contains("POST_NOTIFICATIONS", Read("Constitution.Android/ManifestPermissions.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TodayIsFedFromTheRecordThroughItsOwnMapper()
    {
        var today = Read("Constitution.Android/TodayArea.cs");

        Assert.Contains("TodayPlanFromRecord", Read("Constitution.Android/WorkoutsTab/WorkoutsTabServices.cs"), StringComparison.Ordinal);
        Assert.Contains("TodayModel.Clipping(plan)", today, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableReportGetsItsSentenceOnTheTab()
    {
        var area = Read("Constitution.Android/WorkoutsArea.cs");

        Assert.Contains("WorkoutsRead.Unavailable", area, StringComparison.Ordinal);
    }
}
