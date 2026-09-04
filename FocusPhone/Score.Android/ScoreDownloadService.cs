using Android.App;
using Android.Content;
using AndroidX.Media3.ExoPlayer.Offline;
using AndroidX.Media3.ExoPlayer.Scheduler;

// Android.App has a DownloadManager of its own, and the property this service
// overrides is named for the type — so the type needs a name of its own.
using Media3Downloads = AndroidX.Media3.ExoPlayer.Offline.DownloadManager;

namespace Score;

/// <summary>
/// The foreground service downloads run in.
///
/// It exists because Android will not let an app fetch a gigabyte in the background
/// unasked: a download that matters has to be visible, and a visible download is a
/// foreground service with a notification. That notification is the <b>only</b> one
/// Score adds beyond the media notification — everything else waits in-app as a
/// dog-ear (DESIGN.md §6), and this one is not a choice the app gets to make.
///
/// The name is explicit and frozen, like the playback service's: a download in flight
/// across an in-place upgrade is bound to this component by name.
/// </summary>
[Service(
    Name = "com.storyworthtelling.focusphone.audio.DownloadService",
    Exported = false,
    ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public class ScoreDownloadService : DownloadService
{
    private const int NotificationId = 5140;
    private const string ChannelId = "score-downloads";

    public ScoreDownloadService()
        : base(NotificationId, DefaultForegroundNotificationUpdateInterval, ChannelId,
            Resource.String.downloads_channel, 0)
    {
    }

    protected override Media3Downloads DownloadManager => Downloads.Manager(this);

    // No scheduler: downloads run while the app has them to do and resume when it is
    // next opened. A JobScheduler-backed retry would need Play services on a phone
    // that deliberately does not have them.
    protected override IScheduler? Scheduler => null;

    protected override Notification GetForegroundNotification(
        System.Collections.Generic.IList<Download>? downloads, int notMetRequirements)
    {
        var helper = new DownloadNotificationHelper(this, ChannelId);
        return helper.BuildProgressNotification(
            this, Resource.Drawable.area_music, null, null, downloads ?? [], notMetRequirements)!;
    }
}
