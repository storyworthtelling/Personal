namespace Storyworthtelling.Updates;

/// <summary>What an APK is built for, and what a device installs (P14 W-3.1.1).</summary>
public enum ApkForm
{
    Phone,
    Watch,

    /// <summary>
    /// A form the channel does not recognise, or readings of one APK that disagree. Installed nowhere: refusing is the
    /// one reading that is safe on both ends, since a phone APK on the watch and a watch APK on the phone each replace
    /// the app they share a package with.
    /// </summary>
    Unknown,
}

/// <summary>
/// The form rule. A watch app carries its phone app's package and signature (the Data Layer delivers only between
/// those, P14 plan §2.2) and a higher versionCode band, so by package and version alone Dash is Post's newest build.
/// Each APK therefore says what it is built for — in its sidecar, in its asset's name and, above all, in its own
/// manifest — and each device installs only its own form.
/// </summary>
public static class ApkForms
{
    /// <summary>The sidecar's field. A wire word: frozen, a literal (P14 plan §4).</summary>
    public const string Field = "form";

    /// <summary>The sidecar's two values. Wire words: frozen.</summary>
    public const string PhoneWord = "phone";

    public const string WatchWord = "watch";

    /// <summary>The manifest feature that makes an APK a watch APK: Wear OS's own marker, read with aapt2 by the publish
    /// action and from the APK itself by the phone.</summary>
    public const string WatchFeature = "android.hardware.type.watch";

    /// <summary>
    /// How a watch APK is named on the release. .NET names a signed APK after its package, so Dash and Post would both
    /// go up as <c>…communication-Signed.apk</c>, and the publish replaces an asset by name: without this, whichever
    /// went up last would take the other's place.
    /// </summary>
    public const string WatchAssetSuffix = ".watch.apk";

    /// <summary>The form a sidecar's word names: only the two frozen words, exactly as written; anything else is
    /// <see cref="ApkForm.Unknown"/>. (A sidecar with no form at all is a phone APK: that is the sidecar's rule.)</summary>
    public static ApkForm FromWord(string? word) => word switch
    {
        PhoneWord => ApkForm.Phone,
        WatchWord => ApkForm.Watch,
        _ => ApkForm.Unknown,
    };

    /// <summary>The word for a form, as a sidecar or a log line says it; "unknown" is never written to a sidecar.</summary>
    public static string Word(ApkForm form) => form switch
    {
        ApkForm.Phone => PhoneWord,
        ApkForm.Watch => WatchWord,
        _ => "unknown", // not copy: a log word
    };

    /// <summary>The form an asset's name says. Every APK published before P14 is a phone APK under its own name.</summary>
    public static ApkForm FromAssetName(string assetName) =>
        assetName.EndsWith(WatchAssetSuffix, StringComparison.OrdinalIgnoreCase) ? ApkForm.Watch : ApkForm.Phone;

    /// <summary>The form a manifest's uses-feature list says, required or not: the watch feature makes a watch APK.</summary>
    public static ApkForm FromFeatures(IEnumerable<string?>? features) =>
        features is not null && features.Any(feature => string.Equals(feature, WatchFeature, StringComparison.Ordinal))
            ? ApkForm.Watch
            : ApkForm.Phone;

    /// <summary>
    /// One form from several readings of the same APK: theirs when they all agree, otherwise <see cref="ApkForm.Unknown"/>.
    /// A disagreement means one of the readings is wrong and nothing says which, so the APK is installed nowhere.
    /// </summary>
    public static ApkForm Agree(params ApkForm[] readings)
    {
        if (readings.Length == 0)
        {
            return ApkForm.Unknown;
        }
        var first = readings[0];
        return readings.All(reading => reading == first) ? first : ApkForm.Unknown;
    }
}
