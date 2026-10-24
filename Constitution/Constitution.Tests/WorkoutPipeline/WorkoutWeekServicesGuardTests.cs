using System.Text.RegularExpressions;
using Constitution.Domain.Calendar;
using Constitution.Domain.Diagnostics;
using Constitution.Infrastructure.Workouts;
using Constitution.Tests.Setups;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The join in the head, held by reading its source: it joins through the pure parts, keeps its files under the no-backup folder,
/// swallows nothing, names no colour or size, and opens no way off the phone. Also what may go in the trail from the calendar side.
/// </summary>
public class WorkoutWeekServicesGuardTests
{
    private static readonly Regex Letters = new("[A-Za-z]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static string Services()
    {
        var file = ConstitutionSource.Files(".cs").Single(path => Path.GetFileName(path) == "WorkoutWeekServices.cs");
        return File.ReadAllText(file);
    }

    [Fact]
    public void TheDoorJoinsThroughThePureParts()
    {
        var source = Services();

        Assert.Contains("WorkoutWeekJoin.Prepare(", source, StringComparison.Ordinal);
        Assert.Contains("WorkoutWeekCalendarSeams.Create(", source, StringComparison.Ordinal);
        Assert.Contains("WorkoutWeekDeliverySeams.Create(", source, StringComparison.Ordinal);
        Assert.Contains(".RunAsync(ready.Request, cancellation)", source, StringComparison.Ordinal);
        Assert.Contains("RetryPending()", source, StringComparison.Ordinal);
        Assert.Contains("FileWorkoutWeekReports(", source, StringComparison.Ordinal);
        Assert.Contains("FileWorkoutOverrides(", source, StringComparison.Ordinal);
        Assert.Contains("WorkoutWeekRecordBuilder.From(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSetupAbsentCaseIsTheJoinsTypedStop()
    {
        var join = File.ReadAllText(ConstitutionSource.Files(".cs").Single(path => Path.GetFileName(path) == "WorkoutWeekJoin.cs"));

        Assert.Contains("NotPlannedBecause.NotConfigured", join, StringComparison.Ordinal);
        Assert.Contains("WorkoutWeekPreparation.Stop", Services(), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFileIsUnderTheNoBackupFolder()
    {
        var source = Services();

        Assert.Contains("NoBackupFilesDir", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalFilesDir", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetExternalStorage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesDir.", source.Replace("NoBackupFilesDir", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsSwallowedAndNoMessageIsLogged()
    {
        var source = Services();

        Assert.False(Regex.IsMatch(source, @"catch\s*(\([^)]*\))?\s*\{\s*\}", RegexOptions.None, TimeSpan.FromSeconds(2)), "An empty catch.");
        Assert.DoesNotContain("catch", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToString()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoColourOrSizeLiteralAndNoNewWayOffThePhone()
    {
        var source = Services();

        Assert.False(Regex.IsMatch(source, @"#[0-9A-Fa-f]{6}|0x[0-9A-Fa-f]{6,8}|\bTextSize\b|\b\d+(sp|dp)\b", RegexOptions.None, TimeSpan.FromSeconds(2)));
        foreach (var word in new[] { "RequestPermissions", "UsesPermission", "Intent(", "StartActivity", "SendBroadcast", "ClipboardManager", "NotificationCompat", "HttpClient" })
        {
            Assert.DoesNotContain(word, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheReportFileIsKeptBesideTheOtherRecords()
    {
        Assert.Equal("week-report.json", WorkoutWeekFiles.ReportFile);
        Assert.Equal("owner-moves.json", WorkoutWeekFiles.OverridesFile);
    }

    [Fact]
    public void TheStandingLineStaysOutOfTheTrailWhileItsWordsAreNotInTheVocabulary()
    {
        var standing = CalendarSideStanding.Decide(true, new HouseholdSetupRead.Configured(HouseholdData.Setup()), null, TimeZoneInfo.Utc) with { Assumed = [] };
        var missing = Letters.Matches(standing.Line).Select(match => match.Value.ToLowerInvariant()).Where(word => !TrailLines.Vocabulary.Contains(word)).ToList();

        // The line is logged only once every word of it may leave the phone; until then it stays out, and this fails the day it could go in.
        Assert.NotEmpty(missing);
        Assert.DoesNotContain("Standing", Services(), StringComparison.Ordinal);
    }
}
