using System.Text.Json;

namespace Storyworthtelling.Updates;

/// <summary>
/// The small file published beside each APK on the rolling release, naming its package, versionCode and form, so the
/// phone decides whether it needs the APK before downloading it (efficiency audit §13). Written by the
/// publish-rolling-release action from the APK itself (aapt2, <c>.github/scripts/stage-apks.py</c>); an APK with no
/// readable sidecar is downloaded and read as before.
///
/// The form (P14 W-3.1.1): a sidecar with no <c>form</c> is a phone APK, since every sidecar published before P14 is
/// one. A form that is there but is not exactly "phone" or "watch" is <see cref="ApkForm.Unknown"/> and installed
/// nowhere, rather than read as either: somebody wrote it, and it was not "phone".
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
            return new InstallCandidate(name, code, FormOf(root));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ApkForm FormOf(JsonElement root)
    {
        if (!root.TryGetProperty(ApkForms.Field, out var form))
        {
            return ApkForm.Phone;
        }
        return form.ValueKind == JsonValueKind.String ? ApkForms.FromWord(form.GetString()) : ApkForm.Unknown;
    }
}
