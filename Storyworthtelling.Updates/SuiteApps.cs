namespace Storyworthtelling.Updates;

/// <summary>
/// A release APK to consider installing: its package, its versionCode, and what it is built for. The form has no
/// default: every place that reads an APK says what it read, since a watch APK shares its phone app's package.
/// </summary>
public record InstallCandidate(string Package, long VersionCode, ApkForm Form);

/// <summary>Facts about the app suite that every head has to agree on.</summary>
public static class SuiteApps
{
    /// <summary>
    /// The namespace the update channel trusts. It is the whole trust boundary: an
    /// APK that reached the release from anywhere else is not installed, whoever
    /// uploaded it.
    /// </summary>
    public const string OwnerPackagePrefix = "com.storyworthtelling.";

    public static bool IsOwnApp(string package) =>
        package.StartsWith(OwnerPackagePrefix, StringComparison.Ordinal);
}
