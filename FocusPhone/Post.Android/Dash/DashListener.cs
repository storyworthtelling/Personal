using Android.App;
using Android.Content;
using Android.Gms.Wearable;
using Android.OS;
using Codex.Domain.Post;
using Codex.Domain.Updates;
using Codex.Infrastructure.Dash;
using Codex.Ui;
using Watch.Domain;
using Watch.Domain.Dash;
using Watch.Domain.Updates;

namespace Post;

/// <summary>
/// Post's ear for Dash on the watch (P14 W-4.1.1, B2): Play services binds it for a message on any of Dash's nine paths
/// (<see cref="WatchIdentity.DashPaths"/>), sent by Dash, which carries Post's package and signature. Each message is
/// taken off Play services' thread and waited for here, so the service and its process stay while the act is carried
/// out, and never past its turn (<see cref="DashReplies.TurnWait"/>). Everything it does is <see cref="DashCarrier"/>'s.
/// </summary>
[Service(Name = WatchIdentity.DashListenerName, Exported = true)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashPeoplePath)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashThreadPath)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashSendPath)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashFilePath)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashDeletePath)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashUndoPath)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashReadPath)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashCallNotePath)]
[IntentFilter([MessageClient.ActionMessageReceived], DataScheme = "wear", DataHost = "*", DataPath = WatchIdentity.DashMailPath)]
public sealed class DashListener : WearableListenerService
{
    private const string Tag = "Dash";  // not copy: a log tag

    public override void OnMessageReceived(IMessageEvent messageEvent)
    {
        var path = messageEvent.Path;
        if (path is null || !WatchIdentity.DashPaths.Contains(path, StringComparer.Ordinal))
        {
            return;
        }

        var deadline = new RunDeadline(DateTimeOffset.UtcNow + DashReplies.TurnWait);
        var context = ApplicationContext ?? this;
        var bytes = messageEvent.GetData();
        var node = messageEvent.SourceNodeId;
        var turn = Task.Run(() => DashCarrier.TakeAsync(context, path, node, bytes, deadline));
        if (Looper.MainLooper?.IsCurrentThread == true)
        {
            // Play services calls a listener on a thread of its own; were it ever the main one, the turn runs on its own.
            return;
        }
        if (!turn.Wait(DashReplies.TurnWait))
        {
            HeadLog.Warn(Tag, "DASH a message from the wrist ran past its turn; it finishes on its own");
        }
    }
}

/// <summary>
/// What Post does with each message from the wrist (B2), one at a time: it reads the message for the path it came on
/// (<see cref="DashCodec"/>), asks <see cref="DashInbox"/> what to do with it, carries that out through the code the
/// Mailbox itself uses, and answers on the node that asked. Every decision is the pure half's; this half only reads the
/// stores, carries the plan out, and keeps the book. It logs the contract and the outcome only (<see cref="DashLogLines"/>),
/// never a name, a number, a word or an id.
///
/// <para>The watch-unreachable rule (<see cref="DashReplies"/>): an answer is sent once and waited for a bounded time;
/// one that does not land is counted and dropped, never queued or tried again. Post sends the wrist nothing it did not
/// ask for.</para>
/// </summary>
internal static class DashCarrier
{
    private const string Tag = "Dash";  // not copy: a log tag

    // One message at a time, by the gate Flyleaf's update runs use: a duplicated act waits for the first, and then finds
    // it in the book, so it is done once.
    private static readonly UpdateRunGate OneAtATime = new();

    private static readonly DashReplies Replies = new();

    public static async Task TakeAsync(Context context, string path, string? node, byte[]? bytes, RunDeadline deadline)
    {
        try
        {
            var read = DashCodec.Decode(path, bytes);
            if (read.Message is not { } message)
            {
                HeadLog.Warn(Tag, DashLogLines.Receipt(path, read, DashEnd.Post));
                return;
            }
            HeadLog.Info(Tag, DashLogLines.Receipt(path, read, DashEnd.Post));

            var ran = await OneAtATime.TryRunAsync(
                () => CarryAsync(context, node, message, deadline), deadline.Cut(DateTimeOffset.UtcNow, DashReplies.GateWait));
            if (!ran)
            {
                // The one before it held the gate past this one's wait: the wrist asks or acts again.
                HeadLog.Warn(Tag, DashLogLines.Outcome(path, DashOutcome.Busy));
            }
        }
        catch (Exception e)
        {
            // A store that would not read, or anything unforeseen: its shape is logged, never its message.
            HeadLog.Quiet(Tag, "A message from the wrist stopped; the wrist asks or acts again", e);
        }
    }

    private static async Task CarryAsync(Context context, string? node, DashMessage message, RunDeadline deadline)
    {
        var bridge = GmBridgeClient.Shared(context);
        if (message is DashSend && bridge.IsPaired && !bridge.IsConnected)
        {
            // The message may itself have started Post: a send waits a moment for the bridge, as the phone's would go by it.
            bridge.ConnectIfPaired();
            await BridgeUpAsync(bridge, deadline);
        }

        var acts = DashBook.Acts(context);
        var conditions = new DashConditions(DateTimeOffset.UtcNow, DogEar.UndoWindow(context), bridge.IsConnected, TextCourier.CanSendSms(context));
        switch (DashInbox.Turn(message, () => DashSnapshots.Relay(context), acts, conditions))
        {
            case DashAnswer answer:
                HeadLog.Info(Tag, DashLogLines.Outcome(answer.Path, answer.Outcome, answer.Count));
                if (answer.Reply is DashSent)
                {
                    DashBook.Save(context);
                }
                await ReplyAsync(context, node, answer.Reply, deadline);
                break;
            case DashSendTurn send:
                await ReplyAsync(context, node, Send(context, send, acts, conditions), deadline);
                break;
            case DashActTurn act:
                Carry(context, act, conditions.Now);
                break;
            case DashUndoTurn undo:
                TakeBack(context, undo.Plan);
                break;
            case DashReadTurn read:
                Mark(context, read.Plan);
                break;
            case DashNoteTurn note:
                Keep(context, note.Plan);
                break;
            case DashNoTurn { Outcome: { } outcome } none:
                HeadLog.Info(Tag, DashLogLines.Outcome(none.Path, outcome));
                break;
        }
    }

    // The phone's own road. The book says it is being sent before it goes, so a send heard again is never sent twice.
    private static DashSent Send(Context context, DashSendTurn send, DashActs acts, DashConditions conditions)
    {
        DashBook.Save(context);
        var delivery = TextCourier.Deliver(
            context, send.Plan.Recipients, send.Plan.ConversationId, send.Send.Text, TextSending.FromWrist(), conditions.CanSendSms);
        var sent = DashInbox.Delivered(send.Send, delivery, acts);
        DashBook.Save(context);
        HeadLog.Info(Tag, DashLogLines.Send(sent.Outcome));
        if (delivery.Went)
        {
            // As after the phone's own send: the source is brought up to date at once.
            Syncs.Later(context, SyncMoment.Delay(PostAction.Sent, conditions.PhoneWindow));
        }
        return sent;
    }

    // A filing is the Mailbox's (ghost kept, tombstone raised, hidden at once); a delete is held for the window and then
    // deleted at the source. Either owes the source its sync once the window has closed.
    private static void Carry(Context context, DashActTurn act, DateTimeOffset now)
    {
        var plan = act.Plan;
        HeadLog.Info(Tag, DashLogLines.Outcome(act.Path, plan.Outcome, plan.Items.Count));
        switch (plan.Outcome)
        {
            case DashOutcome.Filed:
                foreach (var item in plan.Items)
                {
                    Filing.File(context, item, now);
                }
                break;
            case DashOutcome.Held:
                DashHolds.Hold(context, act.Act, plan.Items, now + plan.Window);
                break;
            default:
                // Again or unknown: nothing was taken, and the book is as it was.
                return;
        }
        DashBook.Save(context);
        Syncs.Later(context, plan.SyncAfter);
    }

    // An undo takes back exactly what its act took, inside its window: unfiled, or let go from the delete buffer.
    private static void TakeBack(Context context, DashUndoPlan plan)
    {
        HeadLog.Info(Tag, DashLogLines.Outcome(WatchIdentity.DashUndoPath, plan.Outcome, plan.Items.Count));
        if (plan.Outcome != DashOutcome.TakenBack)
        {
            return;
        }

        foreach (var item in plan.Items)
        {
            if (plan.Kind == DashActKind.File)
            {
                Filing.Unfile(context, item.SourceId);
            }
            else
            {
                SourceDeletes.Held.Undo(item.Kind, item.SourceId);
            }
        }
        DashBook.Save(context);
    }

    // Read on the watch is read on the phone (A14), through what the wrist showed, in the store the Mailbox reads.
    private static void Mark(Context context, DashReadPlan plan)
    {
        HeadLog.Info(Tag, DashLogLines.Outcome(WatchIdentity.DashReadPath, plan.Outcome));
        if (plan is { Outcome: DashOutcome.Marked, ThreadKey: { } key })
        {
            using var database = TimelineDatabase.Open(context);
            database.ReadState.MarkRead(key, plan.Stamp);
        }
    }

    // A note from the wrist is kept where the phone keeps the owner's call notes.
    private static void Keep(Context context, DashNotePlan plan)
    {
        HeadLog.Info(Tag, DashLogLines.Outcome(WatchIdentity.DashCallNotePath, plan.Outcome));
        if (plan is { Outcome: DashOutcome.Kept, Note: { } note })
        {
            using var database = TimelineDatabase.Open(context);
            database.Cache.Upsert([note]);
        }
    }

    // Once, on the node that asked, waited for a bounded time; a miss is counted and dropped.
    private static async Task ReplyAsync(Context context, string? node, DashMessage reply, RunDeadline deadline)
    {
        if (string.IsNullOrEmpty(node))
        {
            HeadLog.Warn(Tag, DashLogLines.Outcome(reply.Path, DashOutcome.Unreached, Replies.Missed()));
            return;
        }

        try
        {
            await BoundedAsync(WearableClass.GetMessageClient(context).SendMessageAsync(node, reply.Path, DashCodec.Encode(reply)), deadline.Cut(DateTimeOffset.UtcNow, DashReplies.ReplyWait));
            Replies.Landed();
            HeadLog.Info(Tag, DashLogLines.Sent(reply.Path));
        }
        catch (Exception e) when (e is Java.Lang.Exception or TimeoutException)
        {
            HeadLog.Warn(Tag, DashLogLines.NotSent(reply.Path, e.GetType().Name));
            HeadLog.Warn(Tag, DashLogLines.Outcome(reply.Path, DashOutcome.Unreached, Replies.Missed()));
        }
    }

    // A paired bridge given a moment to come up, never past the turn.
    private static async Task BridgeUpAsync(GmBridgeClient bridge, RunDeadline deadline)
    {
        var until = DateTimeOffset.UtcNow + deadline.Cut(DateTimeOffset.UtcNow, DashReplies.BridgeWait);
        while (!bridge.IsConnected && DateTimeOffset.UtcNow < until)
        {
            await Task.Delay(DashReplies.BridgePoll);
        }
    }

    // A Data Layer call waited for at most its own wait: one that never answers holds nothing. Play services goes on with
    // the call; only the wait ends, and what the call throws after that is read rather than left unobserved.
    private static async Task<T> BoundedAsync<T>(Task<T> call, TimeSpan wait)
    {
        try
        {
            return await call.WaitAsync(wait);
        }
        catch (TimeoutException)
        {
            Observe(call);
            throw;
        }
    }

    // A call the turn stopped waiting for: its fault, if it comes, is logged by type, never thrown into nothing.
    private static void Observe(Task abandoned) =>
        abandoned.ContinueWith(
            static task => HeadLog.Quiet(Tag, "A call to the wrist it stopped waiting for later failed", task.Exception!.InnerException ?? task.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
