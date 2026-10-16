using Android.Content;
using Android.Content.PM;
using AndroidX.Core.App;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// Reads the phone's side of <see cref="TextsStatus"/>: who holds the SMS role, whether the
/// bridge is paired and up, whether Post may read notifications, and whether Google Messages
/// can run at all. Each reading is independent, so one that fails does not hide the rest.
/// </summary>
internal static class TextsStatusReader
{
    private const string GoogleMessagesPackage = "com.google.android.apps.messaging";

    public static TextsStatus Read(Context context)
    {
        var bridge = GmBridgeClient.Shared(context);
        return new TextsStatus(
            HoldsSmsRole: SmsRoleAccess.Status(context) == SmsRoleStatus.Held,
            BridgePaired: bridge.IsPaired,
            BridgeConnected: bridge.IsConnected,
            ListenerEnabled: NotificationManagerCompat.GetEnabledListenerPackages(context)
                .Contains(context.PackageName ?? string.Empty),
            Messages: MessagesState(context));
    }

    private static MessagesAppState MessagesState(Context context)
    {
        var packages = context.PackageManager!;
        ApplicationInfo info;
        try
        {
            info = packages.GetApplicationInfo(GoogleMessagesPackage, (PackageInfoFlags)0);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return MessagesAppState.NotInstalled;
        }
        if (!info.Enabled)
        {
            return MessagesAppState.Disabled;
        }
        // Flyleaf's lockdown suspends what it does not clear, and a suspended Messages can
        // neither keep RCS nor post the notifications Post reads.
        return (info.Flags & ApplicationInfoFlags.Suspended) != 0
            ? MessagesAppState.Suspended
            : MessagesAppState.Installed;
    }
}
