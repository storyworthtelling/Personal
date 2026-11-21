using Android.Content;
using Android.Content.PM;
using Android.Gms.Wearable;
using Watch.Domain;
using Watch.Domain.Updates;

namespace Point.Installer;

/// <summary>Records an outcome, logs it and tells the phone.</summary>
internal static class Outcomes
{
    public static async Task RecordAsync(Context context, WatchInstallResult result, bool logged = false)
    {
        ResultStore.Record(context, result);
        if (!logged)
        {
            InstallerLog.Info(UpdateLines.Install(result.Package, result.VersionCode, result.Outcome, result.Status));
        }
        await Reporter.SendAsync(context, null);
    }
}

/// <summary>The suite apps on the watch, as the plan reads them: versionCode, and whether this installer is of record.</summary>
internal static class InstalledApps
{
    public static IReadOnlyList<InstalledApp> Read(Context context)
    {
        var packages = context.PackageManager!;
        var own = context.PackageName;
        var apps = new List<InstalledApp>();
        foreach (var package in WatchIdentity.Packages)
        {
            if (Find(packages, package) is not { } info)
            {
                continue;
            }
            var installer = packages.GetInstallSourceInfo(package).InstallingPackageName;
            apps.Add(new InstalledApp(package, info.LongVersionCode, string.Equals(installer, own, StringComparison.Ordinal)));
        }
        return apps;
    }

    private static PackageInfo? Find(PackageManager packages, string package)
    {
        try
        {
            return packages.GetPackageInfo(package, PackageManager.PackageInfoFlags.Of(0L));
        }
        catch (PackageManager.NameNotFoundException)
        {
            // Not on the watch: the ordinary case for every app before its first install.
            return null;
        }
    }
}

/// <summary>The last outcome per app, in the installer's preferences, read and written under one lock.</summary>
internal static class ResultStore
{
    private static readonly Lock Gate = new();

    public static IReadOnlyList<WatchInstallResult> Read(Context context)
    {
        lock (Gate)
        {
            return InstallResults.Decode(Preferences(context).GetString(WatchIdentity.ResultsKey, null));
        }
    }

    public static void Record(Context context, WatchInstallResult result)
    {
        lock (Gate)
        {
            var preferences = Preferences(context);
            var results = InstallResults.Record(InstallResults.Decode(preferences.GetString(WatchIdentity.ResultsKey, null)), result);
            preferences.Edit()!.PutString(WatchIdentity.ResultsKey, InstallResults.Encode(results))!.Commit();
        }
    }

    private static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(WatchIdentity.InstallerPreferences, FileCreationMode.Private)!;
}

/// <summary>
/// Each app's current install session and the sessions this installer abandoned (release A fix round, finding 5), in
/// the installer's preferences under the same lock as the results, so a status is judged by the session it is for.
/// </summary>
internal static class SessionStore
{
    private static readonly Lock Gate = new();

    public static InstallSessionRecord Read(Context context)
    {
        lock (Gate)
        {
            return InstallSessions.Decode(Preferences(context).GetString(WatchIdentity.SessionsKey, null));
        }
    }

    public static void Began(Context context, InstallSession session) => Change(context, record => InstallSessions.Began(record, session));

    public static void Abandoning(Context context, int sessionId) => Change(context, record => InstallSessions.Abandoning(record, sessionId));

    public static void Ended(Context context, string package, int sessionId) => Change(context, record => InstallSessions.Ended(record, package, sessionId));

    // Committed, not applied: the platform's status for a session can arrive in a new process moments later.
    private static void Change(Context context, Func<InstallSessionRecord, InstallSessionRecord> change)
    {
        lock (Gate)
        {
            var preferences = Preferences(context);
            var record = change(InstallSessions.Decode(preferences.GetString(WatchIdentity.SessionsKey, null)));
            preferences.Edit()!.PutString(WatchIdentity.SessionsKey, InstallSessions.Encode(record))!.Commit();
        }
    }

    private static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(WatchIdentity.InstallerPreferences, FileCreationMode.Private)!;
}

/// <summary>Sends the watch's report on <c>update/installed</c>: to the node that asked, or to every node connected.</summary>
internal static class Reporter
{
    public static async Task SendAsync(Context context, string? node)
    {
        var report = new InstalledReport(
            [.. InstalledApps.Read(context).Select(app => new WatchApp(app.Package, app.VersionCode))],
            ResultStore.Read(context));
        var bytes = InstalledCodec.Encode(report);

        IReadOnlyList<string> targets = node is not null
            ? [node]
            : [.. (await WearableClass.GetNodeClient(context).GetConnectedNodesAsync()).Select(connected => connected.Id!)];

        var messages = WearableClass.GetMessageClient(context);
        foreach (var target in targets)
        {
            try
            {
                await messages.SendMessageAsync(target, WatchIdentity.UpdateInstalledPath, bytes);
                InstallerLog.Info(UpdateLines.Sent(UpdateLines.InstalledContract));
            }
            catch (Java.Lang.Exception error)
            {
                // The phone is out of reach: it asks again on its next run, so this is a line, not a retry.
                InstallerLog.Warn(UpdateLines.NotSent(UpdateLines.InstalledContract, error.GetType().Name));
            }
        }
    }
}
