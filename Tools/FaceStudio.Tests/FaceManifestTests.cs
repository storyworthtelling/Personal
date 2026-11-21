using System.Xml.Linq;

namespace FaceStudio.Tests;

/// <summary>The face is resources only (WFF setup guide), under its frozen id (P14 §2.2, §4).</summary>
public class FaceManifestTests
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    [Fact]
    public void TheApkHasNoCode()
    {
        var manifest = FaceManifest.Manifest().Root!;
        var application = manifest.Element("application")!;

        Assert.Equal("false", (string)application.Attribute(Android + "hasCode")!);
        Assert.Equal("com.storyworthtelling.watch.point", (string)manifest.Attribute("package")!);
        Assert.Contains(manifest.Elements("uses-feature"), feature => (string?)feature.Attribute(Android + "name") == "android.hardware.type.watch");
        Assert.Empty(application.Elements("activity"));
        Assert.Empty(application.Elements("service"));
        Assert.Empty(application.Elements("receiver"));

        var version = application.Elements("property")
            .Single(property => (string?)property.Attribute(Android + "name") == "com.google.wear.watchface.format.version");
        Assert.Equal("4", (string)version.Attribute(Android + "value")!);
    }

    [Fact]
    public void TheFrozenIdIsALiteral()
    {
        var source = File.ReadAllText(Path.Combine(FaceRead.RepoRoot(), "Tools", "FaceStudio", "PointFace.cs"));

        Assert.Contains("PackageId = \"com.storyworthtelling.watch.point\";", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInfoNamesThePreview()
    {
        var info = FaceManifest.Info().Root!;

        Assert.Equal("WatchFaceInfo", info.Name.LocalName);
        Assert.Equal("@drawable/preview", (string)info.Element("Preview")!.Attribute("value")!);
        Assert.Equal("false", (string)info.Element("Editable")!.Attribute("value")!);
    }

    [Fact]
    public void TheLabelIsPoint()
    {
        var label = FaceManifest.Strings().Root!.Elements("string").Single();

        Assert.Equal("Point", label.Value);
        Assert.Equal("@string/" + (string)label.Attribute("name")!,
            (string)FaceManifest.Manifest().Root!.Element("application")!.Attribute(Android + "label")!);
    }
}
