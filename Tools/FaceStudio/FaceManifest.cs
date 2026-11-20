using System.Xml.Linq;

namespace FaceStudio;

/// <summary>
/// The face's manifest, its <c>watch_face_info.xml</c> and its one string: resources only, no code, as the
/// Watch Face Format setup guide asks (developer.android.com/training/wearables/wff/setup, read 20 Nov 2026).
/// </summary>
public static class FaceManifest
{
    public static XNamespace Android { get; } = "http://schemas.android.com/apk/res/android";

    /// <summary>The string's resource name; the manifest's label points at it.</summary>
    public const string LabelName = "label";

    public static XDocument Manifest() =>
        new(new XElement("manifest",
            new XAttribute(XNamespace.Xmlns + "android", Android),
            new XAttribute("package", PointFace.PackageId),
            new XElement("uses-sdk",
                new XAttribute(Android + "minSdkVersion", PointFace.MinSdk),
                new XAttribute(Android + "targetSdkVersion", PointFace.MinSdk)),
            new XElement("uses-feature", new XAttribute(Android + "name", "android.hardware.type.watch")),
            new XElement("application",
                new XAttribute(Android + "label", "@string/" + LabelName),
                new XAttribute(Android + "hasCode", "false"),
                Property("com.google.wear.watchface.format.version", PointFace.FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Property("com.google.wear.watchface.format.publisher", PointFace.Publisher))));

    /// <summary>The picker's metadata: its preview, and that there is nothing to edit (A34).</summary>
    public static XDocument Info() =>
        new(new XElement("WatchFaceInfo",
            Value("Preview", "@drawable/preview"),
            Value("AvailableInRetail", "false"),
            Value("MultipleInstancesAllowed", "false"),
            Value("Editable", "false")));

    public static XDocument Strings() =>
        new(new XElement("resources",
            new XElement("string", new XAttribute("name", LabelName), PointFace.Label)));

    private static XElement Property(string name, string value) =>
        new("property", new XAttribute(Android + "name", name), new XAttribute(Android + "value", value));

    private static XElement Value(string name, string value) => new(name, new XAttribute("value", value));
}
