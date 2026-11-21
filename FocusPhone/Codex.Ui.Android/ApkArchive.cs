using Android.Content;
using Android.Content.PM;
using Storyworthtelling.Updates;

namespace Codex.Ui;

/// <summary>
/// What an APK on disk is, read from the APK itself: its package, its versionCode and its form (P14 W-3.1.1).
///
/// The form is read twice and the two must agree: once by the platform, from the manifest's uses-feature list
/// (<see cref="PackageInfo.ReqFeatures"/>, filled only when <see cref="PackageInfoFlags.Configurations"/> is asked
/// for), and once from the compiled manifest's own bytes (<see cref="ApkManifest"/>). Either alone could be wrong on
/// some Android version without anything failing; a disagreement reads as <see cref="ApkForm.Unknown"/>, which no
/// device installs. Shared by every phone head that installs from the release (Flyleaf for the suite, Score for itself).
/// </summary>
public static class ApkArchive
{
    /// <returns>The APK as a candidate, or null when the platform cannot read it as an APK at all.</returns>
    public static InstallCandidate? Read(Context context, string path)
    {
        var info = context.PackageManager!.GetPackageArchiveInfo(path, PackageInfoFlags.Configurations);
        if (info?.PackageName is null)
        {
            return null;
        }

        ApkForm scanned;
        using (var apk = File.OpenRead(path))
        {
            scanned = ApkManifest.FormOf(apk);
        }
        var declared = ApkForms.FromFeatures(info.ReqFeatures?.Select(feature => feature.Name));
        return new InstallCandidate(info.PackageName, info.LongVersionCode, ApkForms.Agree(scanned, declared));
    }
}
