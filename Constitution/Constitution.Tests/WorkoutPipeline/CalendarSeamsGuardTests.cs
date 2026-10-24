using System.Text.RegularExpressions;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// The head's calendar-side seams, held by reading the source (the head cannot run here): everything it keeps is under the
/// no-backup folder, it takes every real implementation, nothing is swallowed or logged, and it adds no permission.
/// </summary>
public class CalendarSeamsGuardTests
{
    private const string FileName = "WorkoutWeekCalendarSeams.cs";

    private static string Source()
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => Path.GetFileName(path) == FileName);
        Assert.True(file is not null, FileName + " was not found under Constitution/.");
        return File.ReadAllText(file!);
    }

    [Theory]
    [InlineData("GoogleHouseholdCalendars")]
    [InlineData("GoogleCalendarWriter")]
    [InlineData("GoogleWorkoutEventEditor")]
    [InlineData("FileWrittenKeys")]
    [InlineData("FileKeyAssignments")]
    [InlineData("FileWorkoutLearning")]
    [InlineData("ReadBackServices.LedgerOf")]
    [InlineData("BodyRecoveryReadSource")]
    [InlineData("BodyServices.CreateStore")]
    [InlineData("UnavailableConditions")]
    [InlineData("UnavailableHotelGyms")]
    [InlineData("PlanningSetupFile.Read")]
    [InlineData("CalendarSideStanding.Decide")]
    [InlineData("GoogleCalendarServices.Create")]
    public void TheRealImplementationIsUsed(string name)
    {
        Assert.Contains(name, Source(), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFileIsUnderTheNoBackupFolder()
    {
        var source = Source();

        Assert.Contains("NoBackupFilesDir", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesDir.AbsolutePath", source.Replace("NoBackupFilesDir", string.Empty), StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalFilesDir", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CacheDir", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsSwallowed()
    {
        var source = Source();

        Assert.DoesNotMatch(new Regex(@"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{\s*\}"), source);
        Assert.DoesNotContain("catch (Exception)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoColourOrSizeLiteralsAndNoUnderscores()
    {
        var source = Source();

        Assert.DoesNotMatch(new Regex(@"#[0-9A-Fa-f]{6}\b"), source);
        Assert.DoesNotMatch(new Regex(@"\b(TextSize|FontSize|SetTextSize)\b"), source);
        Assert.DoesNotMatch(new Regex(@"\b[A-Za-z]+_[A-Za-z_]+\b"), source);
    }

    [Fact]
    public void NoLogOrWayOffThePhoneAndNoSecretIsNamed()
    {
        var source = Source();

        foreach (var forbidden in new[] { "Log.", "Console.", "Debug.", "Intent", "Notification", "Clipboard", "SharedPreferences", "Passphrase", "ApiKey", "DeviceToken", "Vault" })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoPermissionIsAdded()
    {
        var manifest = ConstitutionSource.Files(".xml").Single(path => Path.GetFileName(path) == "AndroidManifest.xml");

        Assert.DoesNotContain("calendar", File.ReadAllText(manifest), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uses-permission", Source(), StringComparison.Ordinal);
    }
}
