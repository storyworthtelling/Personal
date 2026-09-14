using global::Android.App;
using global::Android.Content;
using global::Android.OS;
using AndroidX.Core.App;
using Folio.Domain.Identity;

namespace Folio.Android;

/// <summary>
/// The notification the pipeline wears while it reads.
///
/// <para>It exists because of the clock, not because of the design: a background worker is killed at
/// ten minutes and a read measures about fifteen (F49), so the work has to run in the foreground —
/// and foreground work on Android must be visible. Having to show something turned out to be the
/// right outcome anyway. The phone is reading a novel; a line saying which voice is reading it is
/// more honest than a silent quarter of an hour, and it is DESIGN.md §5's status sentence in the one
/// place the app cannot draw its own.</para>
///
/// <para>Low importance, no sound, no vibration: this is a status line, not an event.</para>
/// </summary>
internal static class ReadingNotification
{
    public const int Id = 1913;
    private const string ChannelId = FolioIdentity.ApplicationId + ".reading";

    public static Notification Build(Context context, string sentence)
    {
        EnsureChannel(context);
        return new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle("Folio")!
            .SetContentText(sentence)!
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownload)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetPriority((int)NotificationPriority.Low)!
            .Build();
    }

    private static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, "Reading", NotificationImportance.Low)
        {
            Description = "Shown while Folio is reading a manuscript.",
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }
}
