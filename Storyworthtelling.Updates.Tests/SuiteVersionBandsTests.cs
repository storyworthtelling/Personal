namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// The band table itself: who holds which numbering space. Band 1 is the one deliberate shared
/// band among phone apps (the five that shipped under the old run_number schemes); every other
/// phone band belongs to exactly one app, so a new app can never be refused because of another's.
///
/// <para>P14 added the second form. A watch app shares its phone app's package (PC1), so the table
/// is keyed by package <i>and</i> form, and every watch APK sits in the one watch band, 11, clear of
/// every phone band (PC4): a versionCode is compared per package per device, so the watch's codes never
/// meet the phone's, and one band for every watch APK is the watch's equivalent of band 1.</para>
/// </summary>
public class SuiteVersionBandsTests
{
    private const string ConstitutionPackage = "com.storyworthtelling.constitution";

    private const string LauncherPackage = "com.storyworthtelling.focusphone.launcher";

    private const string FacePackage = "com.storyworthtelling.watch.point";

    [Fact]
    public void BandsAreUnique()
    {
        var table = SuiteVersionBands.All;
        Assert.NotEmpty(table);

        var twice = table.GroupBy(a => (a.Package, a.Form)).Where(g => g.Count() > 1).Select(g => $"{g.Key.Package} ({g.Key.Form})").ToList();
        Assert.True(twice.Count == 0, $"A package appears twice in one form: {string.Join(", ", twice)}");

        // Band 1 is the single legacy shared phone band and band 11 the watch's; every other band must hold exactly one app.
        var shared = table.Where(a => a.Band != 1 && a.Band != SuiteVersionBands.WatchBand)
            .GroupBy(a => a.Band)
            .Where(g => g.Count() > 1)
            .Select(g => $"band {g.Key}: {string.Join(", ", g.Select(a => a.Package))}")
            .ToList();

        Assert.True(shared.Count == 0, $"Bands shared beyond the legacy band 1 and the watch band:\n{string.Join("\n", shared)}");

        // Non-vacuous: the rule must actually have bands to judge beyond the two shared ones.
        Assert.True(table.Count(a => a.Band != 1 && a.Band != SuiteVersionBands.WatchBand) >= 3);
    }

    [Fact]
    public void EveryEntrySaysItsForm()
    {
        Assert.All(SuiteVersionBands.All, band => Assert.True(band.Form is ApkForm.Phone or ApkForm.Watch,
            $"{band.Package} is recorded as {band.Form}, which no device installs."));
    }

    [Fact]
    public void EveryWatchApkIsInTheWatchBand()
    {
        var watch = SuiteVersionBands.All.Where(a => a.Form == ApkForm.Watch).ToList();

        // Non-vacuous: the installer and the face are published from build-watch.yml.
        Assert.Contains(watch, a => a.Package == LauncherPackage);
        Assert.Contains(watch, a => a.Package == FacePackage);
        Assert.All(watch, a => Assert.Equal(SuiteVersionBands.WatchBand, a.Band));
    }

    [Fact]
    public void NoPhoneAppIsInTheWatchBand()
    {
        var phones = SuiteVersionBands.All.Where(a => a.Form == ApkForm.Phone && a.Band == SuiteVersionBands.WatchBand)
            .Select(a => a.Package).ToList();

        Assert.True(phones.Count == 0, $"Band {SuiteVersionBands.WatchBand} is the watch's, but holds: {string.Join(", ", phones)}");
        Assert.InRange(SuiteVersionBands.WatchBand, VersionCode.MinBand, VersionCode.MaxBand);
    }

    [Fact]
    public void TheInstallerKeepsFlyleafsPhoneBand()
    {
        // One package, two forms, two bands: the installer is Flyleaf's package on the watch (PC8), and recording it must
        // not move Flyleaf's own phone entry.
        var launcher = SuiteVersionBands.All.Where(a => a.Package == LauncherPackage).OrderBy(a => a.Form).ToList();

        Assert.Equal(
            [new AppVersionBand(LauncherPackage, 1, 100_163, ApkForm.Phone), new AppVersionBand(LauncherPackage, SuiteVersionBands.WatchBand, 0, ApkForm.Watch)],
            launcher);
    }

    [Fact]
    public void ConstitutionIsBandTen()
    {
        var constitution = Assert.Single(SuiteVersionBands.All, a => a.Package == ConstitutionPackage && a.Form == ApkForm.Phone);

        Assert.Equal(new AppVersionBand(ConstitutionPackage, 10, 0, ApkForm.Phone), constitution);

        var others = SuiteVersionBands.All.Where(a => a.Band == 10 && a.Package != ConstitutionPackage)
            .Select(a => a.Package).ToList();
        Assert.True(others.Count == 0, $"Band 10 is also used by: {string.Join(", ", others)}");
    }
}
