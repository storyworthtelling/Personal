using Android.App;
using Android.Content;
using Android.OS;
using Constitution.Domain.Notices;
using Constitution.Settings;

namespace Constitution.Waiting;

/// <summary>
/// Posts the brief's short list as ordinary notifications (C-9.1.3; D83, D91; PC1) and takes each one down
/// when its cause goes. What to post is <see cref="NoticeNotifications.Plan"/>'s call, from the process-wide
/// <see cref="ConnectionNoticeBoard"/>; this class only talks to the shade. Every word comes from the plan's
/// fixed text and the role rides on the notification as an extra, so Flyleaf's bookmark can wear it. On the
/// owner's locked phone the lockdown suppresses the alert; the notification still exists, and the bookmark
/// is how it shows.
///
/// <para><b>No number</b> can get in (C-7.4.3): nothing here formats text, counts, or sets a badge number, and
/// a test reads this file for that. On a fresh process the board is empty, so anything an earlier process left
/// in the shade is taken down at <see cref="Attach"/>; a cause that still stands raises its notice again.</para>
///
/// <para>Device-gated: nothing here has met a phone. The decision it carries out is unit-tested.</para>
/// </summary>
internal static class NoticePoster
{
    private static readonly object Gate = new();
    private static readonly HashSet<NoticeKind> Posted = [];
    private static Context? appContext;
    private static bool asked;

    /// <summary>Starts watching the board. Safe to call from every entry point; only the first call does anything.</summary>
    public static void Attach(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (Gate)
        {
            if (appContext is not null)
            {
                return;
            }

            appContext = context.ApplicationContext ?? context;
        }

        var manager = (NotificationManager)appContext.GetSystemService(Context.NotificationService)!;
        foreach (var kind in NoticeNotifications.ShortList)
        {
            manager.Cancel(NoticeNotifications.For(kind)!.Id);
        }

        EnsureChannel(manager);
        ConnectionNoticeBoard.Observe(Sync);
    }

    /// <summary>Whether the platform will show Constitution's notifications at all (the runtime permission, API 33+).</summary>
    public static bool MayPost(Context context) =>
        !OperatingSystem.IsAndroidVersionAtLeast(33)
        || context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Android.Content.PM.Permission.Granted;

    /// <summary>Asks for the permission once, from a screen the owner is looking at.</summary>
    public static void AskIfNeeded(Activity activity)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && !asked && !MayPost(activity))
        {
            asked = true;
            activity.RequestPermissions([Android.Manifest.Permission.PostNotifications], AskRequestCode);
        }
    }

    /// <summary>The request code the permission ask uses.</summary>
    public const int AskRequestCode = 7417;

    private static void Sync(IReadOnlyList<Notice> waiting)
    {
        Context context;
        NoticeNotificationPlan plan;
        lock (Gate)
        {
            if (appContext is null)
            {
                return;
            }

            context = appContext;
            plan = NoticeNotifications.Plan(waiting, Posted);
            foreach (var note in plan.Post)
            {
                Posted.Add(note.Kind);
            }

            foreach (var kind in plan.Cancel)
            {
                Posted.Remove(kind);
            }
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        foreach (var kind in plan.Cancel)
        {
            manager.Cancel(NoticeNotifications.For(kind)!.Id);
        }

        if (!MayPost(context))
        {
            return;
        }

        foreach (var note in plan.Post)
        {
            var extras = new Bundle();
            extras.PutString(NoticeNotifications.RoleExtraKey, note.RoleWire);
            var open = PendingIntent.GetActivity(
                context, note.Id, context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!), PendingIntentFlags.Immutable);

            var built = new Notification.Builder(context, NoticeNotifications.ChannelId)
                .SetSmallIcon(context.ApplicationInfo!.Icon)
                .SetContentTitle(note.Title)
                .SetContentText(note.Text)
                .SetAutoCancel(false)
                .SetOnlyAlertOnce(true)
                .SetContentIntent(open)
                .AddExtras(extras)
                .Build();
            manager.Notify(note.Id, built);
        }
    }

    private static void EnsureChannel(NotificationManager manager)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var channel = new NotificationChannel(NoticeNotifications.ChannelId, "Notices", NotificationImportance.Default)
        {
            Description = "Something Constitution needs you to see.",
        };
        manager.CreateNotificationChannel(channel);
    }
}
