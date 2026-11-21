using Android.Content;
using Android.Gms.Wearable;

namespace Point.Installer;

/// <summary>The received APKs' files, in the installer's cache, one per transfer.</summary>
internal static class Transfers
{
    private const string Folder = "update-apk";

    // A transfer whose channel never closed leaves its file behind; one a day old is not coming back.
    private static readonly TimeSpan Stale = TimeSpan.FromDays(1);

    public static string PartFor(Context context, string transfer) => Path.Combine(FolderOf(context), transfer + ".part");

    public static string ApkFor(Context context, string transfer) => Path.Combine(FolderOf(context), transfer + ".apk");

    /// <summary>Asks Play services to write the channel into the transfer's file; the install starts when its input closes.</summary>
    public static async Task ReceiveAsync(Context context, ChannelClient.IChannel channel, string transfer)
    {
        Sweep(context);
        var file = new Java.IO.File(PartFor(context, transfer));
        await WearableClass.GetChannelClient(context).ReceiveFileAsync(channel, Android.Net.Uri.FromFile(file)!, false);
    }

    public static async Task RefuseAsync(Context context, ChannelClient.IChannel channel) =>
        await WearableClass.GetChannelClient(context).CloseAsync(channel);

    public static void Forget(Context context, string transfer)
    {
        File.Delete(PartFor(context, transfer));
        File.Delete(ApkFor(context, transfer));
    }

    public static string CloseWord(int reason) => reason switch
    {
        ChannelClient.ChannelCallback.CloseReasonDisconnected => "disconnected",
        ChannelClient.ChannelCallback.CloseReasonRemoteClose => "closed by the phone",
        ChannelClient.ChannelCallback.CloseReasonLocalClose => "closed on the watch",
        ChannelClient.ChannelCallback.CloseReasonConnectionTimeout => "timed out",
        _ => $"close reason {reason}",
    };

    private static string FolderOf(Context context)
    {
        var folder = Path.Combine(context.CacheDir!.AbsolutePath, Folder);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Sweep(Context context)
    {
        foreach (var file in Directory.EnumerateFiles(FolderOf(context)))
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > Stale)
            {
                File.Delete(file);
            }
        }
    }
}
