using Android.Content;
using Android.Gms.Wearable;
using Watch.Domain;
using Watch.Domain.Dash;

namespace Dash.Wear;

/// <summary>What came back from the phone: its answer, or what stood in the way.</summary>
internal sealed record DashReply(DashMessage? Answer, DashTrouble? Trouble)
{
    public static DashReply Answered(DashMessage answer) => new(answer, null);

    public static DashReply Failed(DashTrouble trouble) => new(null, trouble);
}

/// <summary>
/// Dash's end of the Data Layer (P14 B4; plan §2.5): it finds the phone that carries Post's relay by its capability
/// (<see cref="WatchIdentity.PostCapability"/>), near before through the cloud (<see cref="RelayNodes"/>), sends each
/// message encoded by the same codec Post reads, and hears Post's answers only while it waits for one. Post answers on
/// the path it was asked on, once, to the node that asked (B2), so each ask is matched to its answer by
/// <see cref="DashExchange"/>, and waited for before it is sent, so an answer that comes back at once still finds it.
///
/// <para>Every step is bounded (<see cref="DashWaits"/>) and none runs on the main thread: a screen hands each ask to
/// the pool and is given the reply. Nothing is kept: an answer for a screen that has gone is dropped, and the screen
/// asks again when it is back. It logs the contract and the outcome only (<see cref="DashLogLines"/>).</para>
/// </summary>
internal sealed class PostLink : Java.Lang.Object, MessageClient.IOnMessageReceivedListener
{
    private readonly object gate = new();
    private readonly List<Waiting> waiting = [];
    private bool listening;

    private PostLink()
    {
    }

    public static PostLink Shared { get; } = new();

    /// <summary>Asks the phone and waits for its answer, every step bounded. Never on the main thread.</summary>
    public async Task<DashReply> AskAsync(Context context, DashMessage ask)
    {
        var ticket = DashExchange.Awaits(ask)
            ?? throw new ArgumentException("Nothing answers this message; it is told, not asked.", nameof(ask));
        var waiter = new Waiting(ticket, new TaskCompletionSource<DashMessage?>(TaskCreationOptions.RunContinuationsAsynchronously));
        Listen(context, waiter);
        try
        {
            var trouble = await DeliverAsync(context, ask);
            if (trouble is not null)
            {
                return DashReply.Failed(trouble.Value);
            }

            try
            {
                var answer = await waiter.Answer.Task.WaitAsync(DashExchange.WaitFor(ask));
                return answer is null ? DashReply.Failed(DashTrouble.Unreadable) : DashReply.Answered(answer);
            }
            catch (TimeoutException)
            {
                DashLog.Warn(DashLogLines.Outcome(ask.Path, DashOutcome.NoAnswer));
                return DashReply.Failed(DashTrouble.NoAnswer);
            }
        }
        finally
        {
            Unlisten(context, waiter);
        }
    }

    /// <summary>Tells the phone something nothing answers (a read), bounded; what stood in the way, if anything.</summary>
    public Task<DashTrouble?> TellAsync(Context context, DashMessage message) => DeliverAsync(context, message);

    /// <summary>An answer from the phone, on Play services' listener thread: matched to the ask that waits for it.</summary>
    public void OnMessageReceived(IMessageEvent messageEvent)
    {
        var path = messageEvent.Path;
        if (path is null || !WatchIdentity.DashPaths.Contains(path, StringComparer.Ordinal))
        {
            return;
        }

        var read = DashCodec.Decode(path, messageEvent.GetData());
        if (read.Message is not { } message)
        {
            DashLog.Warn(DashLogLines.Receipt(path, read, DashEnd.Dash));
            Settle(waiter => waiter.Ticket.IsOn(path), null);
            return;
        }

        DashLog.Info(DashLogLines.Receipt(path, read, DashEnd.Dash));
        var answers = DashExchange.Answers(message);
        if (answers is { } ticket)
        {
            Settle(waiter => waiter.Ticket == ticket, message);
        }
    }

    private async Task<DashTrouble?> DeliverAsync(Context context, DashMessage message)
    {
        string? node;
        try
        {
            node = await FindAsync(context);
        }
        catch (Exception failure)
        {
            // Play services refused, or did not answer within its wait: the phone cannot be reached from here.
            DashLog.Warn(DashLogLines.NotSent(message.Path, failure.GetType().FullName ?? nameof(Exception)));
            return DashTrouble.NoPhone;
        }

        if (node is null)
        {
            DashLog.Warn(DashLogLines.Outcome(message.Path, DashOutcome.NoPhone));
            return DashTrouble.NoPhone;
        }

        try
        {
            await BoundedAsync(WearableClass.GetMessageClient(context).SendMessageAsync(node, message.Path, DashCodec.Encode(message)), DashWaits.Deliver);
            DashLog.Info(DashLogLines.Sent(message.Path));
            return null;
        }
        catch (Exception failure)
        {
            // The phone went out of reach between finding it and sending, or the send outlived its wait.
            DashLog.Warn(DashLogLines.NotSent(message.Path, failure.GetType().FullName ?? nameof(Exception)));
            return DashTrouble.NoPhone;
        }
    }

    private static async Task<string?> FindAsync(Context context)
    {
        var capabilities = WearableClass.GetCapabilityClient(context);
        var capability = await BoundedAsync(capabilities.GetCapabilityAsync(WatchIdentity.PostCapability, CapabilityClient.FilterReachable), DashWaits.Find);
        var nodes = capability?.Nodes?.Select(node => new RelayNode(node.Id, node.IsNearby)) ?? [];
        return RelayNodes.Choose(nodes);
    }

    private void Listen(Context context, Waiting waiter)
    {
        lock (gate)
        {
            waiting.Add(waiter);
            if (listening)
            {
                return;
            }
            listening = true;
            WearableClass.GetMessageClient(context.ApplicationContext ?? context).AddListener(this);
        }
    }

    private void Unlisten(Context context, Waiting waiter)
    {
        lock (gate)
        {
            waiting.Remove(waiter);
            if (!listening || waiting.Count > 0)
            {
                return;
            }
            listening = false;
            WearableClass.GetMessageClient(context.ApplicationContext ?? context).RemoveListener(this);
        }
    }

    // Hands the answer, or null for one that would not read, to every ask it is for.
    private void Settle(Func<Waiting, bool> isFor, DashMessage? answer)
    {
        lock (gate)
        {
            foreach (var waiter in waiting.Where(isFor))
            {
                waiter.Answer.TrySetResult(answer);
            }
        }
    }

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

    // A call the wrist stopped waiting for: a failure that comes later is logged by its type, never thrown into nothing.
    private static void Observe(Task abandoned) =>
        abandoned.ContinueWith(
            static task => DashLog.Quiet("DASH a call the wrist stopped waiting for failed later", task.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private sealed record Waiting(DashTicket Ticket, TaskCompletionSource<DashMessage?> Answer);
}
