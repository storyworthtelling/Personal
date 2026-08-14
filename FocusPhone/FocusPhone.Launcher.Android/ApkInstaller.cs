using Android.App;
using Android.Content;
using Android.Content.PM;

namespace FocusPhone.Launcher;

/// <summary>
/// Device-owner silent APK install. It needs neither adb nor unknown-sources, so
/// it is the sanctioned install path both for the update worker and for the
/// admin-mode "Install APK from Downloads" action — and it keeps working after
/// the cutover disables debugging (that is why the manual installer can replace
/// the adb sideload the owner uses during setup). Every session re-whitelists the
/// hard-restricted call-log/SMS permissions so a Communication app (re)install
/// keeps them; the flags are harmless for any other package that never requests
/// them.
/// </summary>
public static class ApkInstaller
{
    public static async Task InstallAsync(Context context, Stream apk)
    {
        var installer = context.PackageManager!.PackageInstaller;
        var sessionParams = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        sessionParams.SetWhitelistedRestrictedPermissions(
            [
                Android.Manifest.Permission.ReadCallLog,
                Android.Manifest.Permission.ReadSms,
                Android.Manifest.Permission.WriteCallLog,
            ]);

        var sessionId = installer.CreateSession(sessionParams);
        using var session = installer.OpenSession(sessionId);
        using (var output = session.OpenWrite("focusphone.apk", 0, -1))
        {
            await apk.CopyToAsync(output);
            session.Fsync(output);
        }

        var statusIntent = new Intent(context, typeof(InstallResultReceiver));
        var pending = PendingIntent.GetBroadcast(
            context, sessionId, statusIntent, PendingIntentFlags.Mutable)!;
        session.Commit(pending.IntentSender);
    }
}
