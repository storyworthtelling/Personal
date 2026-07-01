using System.Text;
using Android.Content.PM;
using Java.Security;

namespace Storyboarder.Android.Google;

// Reads the running APK's package name and signing-certificate SHA-1. Google matches AccountManager
// OAuth token requests against this (package, certificate) pair, so these are the exact values the
// user must register as an Android OAuth client in the Google Cloud Console. Shown on the Settings
// page and in the "app isn't registered" sign-in error so registration is copy-paste.
public static class AppSigningInfo
{
    public static string PackageName =>
        global::Android.App.Application.Context.PackageName ?? "unknown";

    public static string Sha1Fingerprint()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var packageManager = context.PackageManager!;
            var packageName = context.PackageName!;

            var certBytes = SigningCertificate(packageManager, packageName);
            var hash = MessageDigest.GetInstance("SHA-1")!.Digest(certBytes)!;

            var builder = new StringBuilder(hash.Length * 3);
            foreach (var value in hash)
            {
                if (builder.Length > 0)
                    builder.Append(':');
                builder.Append(value.ToString("X2"));
            }
            return builder.ToString();
        }
        catch (Exception ex)
        {
            return $"(unavailable: {ex.Message})";
        }
    }

    private static byte[] SigningCertificate(PackageManager packageManager, string packageName)
    {
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.P)
        {
            var info = packageManager.GetPackageInfo(packageName, PackageInfoFlags.SigningCertificates)!;
            var signers = info.SigningInfo?.GetApkContentsSigners();
            if (signers is { Length: > 0 })
                return signers[0].ToByteArray()!;
        }

#pragma warning disable CS0618 // Signatures is obsolete but is the only option on API 26–27.
        var legacy = packageManager.GetPackageInfo(packageName, PackageInfoFlags.Signatures)!;
        return legacy.Signatures![0].ToByteArray()!;
#pragma warning restore CS0618
    }
}
