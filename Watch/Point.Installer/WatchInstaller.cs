using Android.Content;
using Android.Content.PM;
using Storyworthtelling.Updates;
using Watch.Domain.Updates;

namespace Point.Installer;

/// <summary>
/// One received APK, from the file Play services wrote to a committed install session (P14 W-3.1.2). Every decision is
/// <see cref="WatchUpdatePlan"/>'s: first on the phone's header, then on the APK's own readings (the platform's package,
/// versionCode and uses-feature list, and the manifest's bytes), so a phone APK is refused here whatever the phone
/// decided. The session's result arrives later at <see cref="InstallStatusReceiver"/>.
/// </summary>
internal static class WatchInstaller
{
    public static async Task InstallAsync(Context context, string transfer)
    {
        var part = Transfers.PartFor(context, transfer);
        var apkPath = Transfers.ApkFor(context, transfer);
        try
        {
            ContractRead<ReceivedApk> read;
            using (var framed = File.OpenRead(part))
            using (var apk = File.Create(apkPath))
            {
                read = UpdateApkFrame.Unpack(framed, apk);
            }
            InstallerLog.Info(UpdateLines.Receipt(UpdateLines.ApkContract, read));
            if (read.Message is not { } received)
            {
                return;
            }

            var header = received.Header;
            var installed = InstalledApps.Read(context);
            var decision = WatchUpdatePlan.Decide(header.Candidate, installed);
            if (decision.Install)
            {
                decision = WatchUpdatePlan.Verify(received, Read(context, apkPath), installed);
            }
            if (decision.Refusal is { } refused)
            {
                await Outcomes.RecordAsync(context, new WatchInstallResult(header.Package, header.VersionCode, refused));
                return;
            }

            await CommitAsync(context, apkPath, header, decision);
        }
        finally
        {
            Transfers.Forget(context, transfer);
        }
    }

    // The APK's own readings: the platform's, and the manifest's bytes. Either alone could be wrong on some Android
    // version without anything failing; the plan installs only when they agree with each other and with the header.
    private static ApkReading Read(Context context, string apkPath)
    {
        var info = context.PackageManager!.GetPackageArchiveInfo(
            apkPath, PackageManager.PackageInfoFlags.Of((long)PackageInfoFlags.Configurations));
        ApkForm scanned;
        using (var apk = File.OpenRead(apkPath))
        {
            scanned = ApkManifest.FormOf(apk);
        }
        var declared = ApkForms.FromFeatures(info?.ReqFeatures?.Select(feature => feature.Name));
        return new ApkReading(info?.PackageName, info?.LongVersionCode ?? 0, scanned, declared);
    }

    private static async Task CommitAsync(Context context, string apkPath, UpdateApkHeader header, WatchUpdateDecision decision)
    {
        var installer = context.PackageManager!.PackageInstaller;
        try
        {
            // One prompt per app: a newer build (or the same one sent again) replaces a prompt still waiting. The platform
            // reports an abandoned session as aborted, so each is noted first, and its status is not taken for the app's
            // outcome (release A fix round, finding 5).
            foreach (var waiting in installer.MySessions.Where(session => string.Equals(session.AppPackageName, header.Package, StringComparison.Ordinal)).ToList())
            {
                SessionStore.Abandoning(context, waiting.SessionId);
                installer.AbandonSession(waiting.SessionId);
            }

            var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
            parameters.SetAppPackageName(header.Package);
            parameters.SetSize(header.Length);
            // Silence is asked for only where the platform may grant it: the installer of record, or itself. Whether
            // Wear OS grants it is unverified (risk R2); if it asks anyway the status says so and the wrist is asked once.
            parameters.SetRequireUserAction((int)(decision.RequestsSilence ? PackageInstallUserAction.NotRequired : PackageInstallUserAction.Required));

            var sessionId = installer.CreateSession(parameters);
            // The app's current session from here on: only its status is the build's outcome.
            SessionStore.Began(context, new InstallSession(header.Package, header.VersionCode, sessionId, DateTimeOffset.UtcNow));
            using var session = installer.OpenSession(sessionId);
            using (var output = session.OpenWrite("apk", 0, header.Length))
            using (var apk = File.OpenRead(apkPath))
            {
                await apk.CopyToAsync(output);
                session.Fsync(output);
            }
            session.Commit(InstallStatusReceiver.For(context, sessionId, header, decision).IntentSender);
        }
        catch (Exception error) when (error is IOException or Java.Lang.Exception)
        {
            // The platform refused the session itself (storage, a security check) before any status could arrive.
            InstallerLog.Warn($"{UpdateLines.Install(header.Package, header.VersionCode, WatchInstallOutcome.Failed, InstallerStatus.Failure)} ({error.GetType().Name})");
            await Outcomes.RecordAsync(context, new WatchInstallResult(header.Package, header.VersionCode, WatchInstallOutcome.Failed, InstallerStatus.Failure), logged: true);
        }
    }
}
