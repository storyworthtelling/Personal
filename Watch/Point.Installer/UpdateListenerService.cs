using Android.App;
using Android.Gms.Wearable;
using Watch.Domain;
using Watch.Domain.Updates;

[assembly: Application(Label = InstallPrompt.InstallerLabel, AllowBackup = false)]

namespace Point.Installer;

/// <summary>
/// The installer's ear on the Data Layer (plan §2.5). Play services binds it when the phone opens an
/// <c>update/apk</c> channel or sends an <c>update/installed</c> message, and hears nothing else: the filters name the
/// two frozen paths. An APK is received by Play services into a file named for its transfer, so a process that dies
/// mid-transfer loses nothing; the install starts when the channel's input closes.
/// </summary>
[Service(Name = WatchIdentity.UpdateListenerName, Exported = true)]
[IntentFilter([ChannelClient.ActionChannelEvent], DataScheme = "wear", DataHost = "*", DataPathPrefix = WatchIdentity.UpdateApkPath)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.UpdateInstalledPath)]
public sealed class UpdateListenerService : WearableListenerService
{
    public override void OnChannelOpened(ChannelClient.IChannel channel)
    {
        var transfer = UpdatePaths.TransferOf(channel.Path);
        if (transfer is null)
        {
            InstallerLog.Info(UpdateLines.Receipt(UpdateLines.ApkContract, ContractRead<ReceivedApk>.Refused(ContractRefusal.Unreadable, 0)));
            Background.Run(() => Transfers.RefuseAsync(this, channel));
            return;
        }

        Background.Run(() => Transfers.ReceiveAsync(this, channel, transfer));
    }

    public override void OnInputClosed(ChannelClient.IChannel channel, int closeReason, int appSpecificErrorCode)
    {
        var transfer = UpdatePaths.TransferOf(channel.Path);
        if (transfer is null)
        {
            return;
        }

        if (closeReason != ChannelClient.ChannelCallback.CloseReasonNormal)
        {
            InstallerLog.Info(UpdateLines.Transfer(UpdateLines.ApkContract, Transfers.CloseWord(closeReason)));
            Transfers.Forget(this, transfer);
            return;
        }

        Background.Run(() => WatchInstaller.InstallAsync(this, transfer));
    }

    public override void OnMessageReceived(IMessageEvent messageEvent)
    {
        if (!string.Equals(messageEvent.Path, WatchIdentity.UpdateInstalledPath, StringComparison.Ordinal))
        {
            return;
        }

        var read = InstalledCodec.Decode(messageEvent.GetData());
        InstallerLog.Info(UpdateLines.Receipt(UpdateLines.InstalledContract, read));
        if (read.Message is InstalledAsk)
        {
            var node = messageEvent.SourceNodeId;
            Background.Run(() => Reporter.SendAsync(this, node));
        }
    }
}
