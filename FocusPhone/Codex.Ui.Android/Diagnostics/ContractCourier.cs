using Android.Content;
using Android.Content.PM;
using Storyworthtelling.Diagnostics;

namespace Codex.Ui;

/// <summary>
/// The Android half of <see cref="ContractBroadcast"/> for the native heads: whether the receiving app is installed
/// and whether this app holds the contract's permission, read before a send, because <c>SendBroadcast</c> itself
/// reports nothing (audit W2, 09 L1). The courier logs the answer through <see cref="HeadLog"/>.
/// </summary>
public static class ContractCourier
{
    public static ContractSendOutcome Before(Context context, string receiverPackage, string permission) =>
        ContractBroadcast.Before(Installed(context, receiverPackage), context.CheckSelfPermission(permission) == Permission.Granted);

    private static bool Installed(Context context, string package)
    {
        try
        {
            context.PackageManager!.GetPackageInfo(package, (PackageInfoFlags)0);
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }
}
