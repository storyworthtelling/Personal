namespace Storyworthtelling.Updates.Tests;

// Efficiency audit §13: the update channel downloaded every changed APK (tens of MB each, Flyleaf and Score together
// on every main build) only to learn its package and version. Each APK is now published with a small sidecar naming
// both, and an APK the phone does not need is never downloaded. (Updates still download on any network: owner's
// decision.)
public class ApkSidecarTests
{
    [Fact]
    public void TheSidecarIsNamedAfterItsApk() =>
        Assert.Equal("Flyleaf-Signed.apk.json", ApkSidecar.NameFor("Flyleaf-Signed.apk"));

    [Fact]
    public void ASidecarReadsAsACandidate()
    {
        var candidate = ApkSidecar.Read("""{"package":"com.storyworthtelling.pulp","versionCode":900123}""");

        Assert.Equal(new InstallCandidate("com.storyworthtelling.pulp", 900123, ApkForm.Phone), candidate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"package":"","versionCode":5}""")]
    [InlineData("""{"package":"com.storyworthtelling.pulp"}""")]
    public void AnUnreadableSidecarIsNoCandidate(string json) => Assert.Null(ApkSidecar.Read(json));

    [Fact]
    public void ThePublishActionWritesASidecarBesideEveryApk()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md")))
            root = Path.GetDirectoryName(root);
        var action = File.ReadAllText(Path.Combine(root!, ".github", "actions", "publish-rolling-release", "action.yml"));
        // The action stages every APK through one script, which reads it with aapt2 and writes its sidecar
        // (ApkStagingTests runs it).
        var script = File.ReadAllText(Path.Combine(root!, ".github", "scripts", "stage-apks.py"));

        Assert.Contains("aapt2", action, StringComparison.Ordinal);
        Assert.Contains("stage-apks.py", action, StringComparison.Ordinal);
        Assert.Contains(".json", script, StringComparison.Ordinal);
        Assert.Contains("versionCode", script, StringComparison.Ordinal);
    }
}
