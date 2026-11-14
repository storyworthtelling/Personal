using System.Text.Json;

namespace Storyworthtelling.Updates;

/// <summary>
/// The small file published beside each APK on the rolling release, naming its package and versionCode, so the
/// phone decides whether it needs the APK before downloading it (efficiency audit §13). Written by the
/// publish-rolling-release action from the APK itself (aapt2); an APK with no readable sidecar is downloaded and read
/// as before.
/// </summary>
public static class ApkSidecar
{
    public static string NameFor(string apkName) => apkName + ".json";

    public static InstallCandidate? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("package", out var package) || package.GetString() is not { Length: > 0 } name
                || !root.TryGetProperty("versionCode", out var version) || !version.TryGetInt64(out var code))
            {
                return null;
            }
            return new InstallCandidate(name, code);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
