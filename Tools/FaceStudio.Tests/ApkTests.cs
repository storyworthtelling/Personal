namespace FaceStudio.Tests;

/// <summary>The face packs into an APK with no code (W-2.1.1), with aapt2 from the Android SDK.</summary>
public class ApkTests
{
    // Release A fix round: build-tools and the workload's packs are version folders, and "9.0.0" sorts after "36.0.0" as
    // text, so the newest is chosen by number.
    [Fact]
    public void TheNewestToolsComeFirstByNumber()
    {
        Assert.Equal(["36.0.0", "34.0.0", "9.0.0"], Apk.NewestFirst(["9.0.0", "36.0.0", "34.0.0"]).Select(Path.GetFileName));
        Assert.Equal(["/sdk/build-tools/36.1.118", "/sdk/build-tools/36.0.0", "/sdk/build-tools/35.0.105"],
            Apk.NewestFirst(["/sdk/build-tools/35.0.105", "/sdk/build-tools/36.0.0", "/sdk/build-tools/36.1.118"]));
    }

    [Fact]
    public void AFolderThatIsNoVersionComesLast()
    {
        Assert.Equal(["10.0.0", "2.0", "debian"], Apk.NewestFirst(["debian", "2.0", "10.0.0"]).Select(Path.GetFileName));
        Assert.Equal(["36.0.0", "36.0.0-rc1"], Apk.NewestFirst(["36.0.0-rc1", "36.0.0"]).Select(Path.GetFileName));
    }

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
