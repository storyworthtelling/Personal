using Android.Content;
using Android.Gms.Wearable;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Dash;
using Codex.Ui;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Domain.Updates;

namespace Post;

/// <summary>
/// Post telling the wrist a text arrived (P14 B8; WD §7, A12), so Dash can give its two short taps: the one message Post
/// sends the wrist unasked. It is told where the phone itself alerts for an arriving text (<see cref="SmsNotifications"/>,
/// after the spam check), named as People waiting names the correspondence (<see cref="DashArrivals"/>), with whether the
/// sender is starred (<see cref="DashStars"/>), and never a name, a number or a word.
///
/// <para>The watch-unreachable rule (B2) holds: the wrist is found by its capability among the nodes in reach, told once on
/// the pool with every wait bounded, and a wrist out of reach is simply not told; nothing is queued or tried again. The
/// line says the contract and the outcome only.</para>
/// </summary>
internal static class WristArrivals
{
    private const string Tag = "Dash";  // not copy: a log tag

    /// <summary>How long finding the wrist and telling it may take, all told.</summary>
    private static readonly TimeSpan Turn = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan FindWait = TimeSpan.FromSeconds(5);

    public static void Tell(Context context, TimelineItem arriving)
    {
        var application = context.ApplicationContext ?? context;
        _ = Task.Run(() => TellAsync(application, arriving));
    }

    private static async Task TellAsync(Context context, TimelineItem arriving)
    {
        try
        {
            var deadline = new RunDeadline(DateTimeOffset.UtcNow + Turn);
            var arrived = DashArrivals.For(arriving, key => DashStars.IsStarred(context, key));

            var capabilities = WearableClass.GetCapabilityClient(context);
            var capability = await DashCarrier.BoundedAsync(capabilities.GetCapabilityAsync(WatchIdentity.DashCapability, CapabilityClient.FilterReachable), deadline.Cut(DateTimeOffset.UtcNow, FindWait));
            var node = RelayNodes.Choose(capability?.Nodes?.Select(found => new RelayNode(found.Id, found.IsNearby)) ?? []);
            if (node is null)
            {
                // No wrist in reach: it is not told, and it buzzes for nothing it missed.
                HeadLog.Info(Tag, DashLogLines.Outcome(WatchIdentity.DashArrivedPath, DashOutcome.Unreached));
                return;
            }
            await DashCarrier.SendOnceAsync(context, node, arrived, deadline);
        }
        catch (Exception e)
        {
            // The contacts provider or Play services refusing: the wrist is not told, and the failure's shape is logged.
            HeadLog.Quiet(Tag, "DASH a text arrived but the wrist wasn’t told", e);
        }
    }
}
