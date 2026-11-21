namespace FaceStudio.Tests;

/// <summary>The face packs into an APK with no code (W-2.1.1), with aapt2 from the Android SDK.</summary>
public class ApkTests
{
    [Fact]
    public void TheBuiltApkHasNoCode()
    {
        var aapt2 = Apk.FindAapt2();
        var androidJar = Apk.FindAndroidJar();
        if (aapt2 is null || androidJar is null)
        {
            // CI's face build sets FACESTUDIO_APK_REQUIRED=1, so there a missing toolchain fails rather than passes quietly.
            Assert.True(Environment.GetEnvironmentVariable("FACESTUDIO_APK_REQUIRED") != "1",
                $"No aapt2 ({aapt2 ?? "missing"}) or android.jar ({androidJar ?? "missing"}); run FocusPhone/scripts/setup-android-build.sh.");

            // No Android SDK here: the manifest test above is what runs.
            return;
        }

        var work = Directory.CreateTempSubdirectory("facestudio-");
        try
        {
            var face = Path.Combine(work.FullName, "face");
            FaceFiles.Write(FaceRead.RepoRoot(), face);
            var apk = Path.Combine(work.FullName, "point.apk");

            Apk.Build(face, apk, aapt2, androidJar, versionCode: 1, versionName: "1.0");

            var manifest = Apk.DumpManifest(apk, aapt2);
            Assert.Matches(@"android:hasCode\(0x[0-9a-f]+\)=false", manifest);
            Assert.Contains("com.storyworthtelling.watch.point", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("classes.dex", ApkEntries(apk));
            Assert.Contains("res/raw/watchface.xml", ApkEntries(apk));
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    private static List<string> ApkEntries(string apk)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(apk);
        return archive.Entries.Select(entry => entry.FullName).ToList();
    }
}
