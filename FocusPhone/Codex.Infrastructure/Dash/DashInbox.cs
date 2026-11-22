using Codex.Domain.Post;
using Watch.Domain;
using Watch.Domain.Dash;

namespace Codex.Infrastructure.Dash;

/// <summary>
/// What the phone is like as a message from the wrist is taken: the time, the phone's own undo window (the suite's one
/// window on this device), whether Google Messages' bridge is up, and whether Post may send a plain text.
/// </summary>
public sealed record DashConditions(DateTimeOffset Now, TimeSpan PhoneWindow, bool BridgeConnected, bool CanSendSms);

/// <summary>What Post's listener is to do with one message from the wrist (P14 B2).</summary>
public abstract record DashTurn(string Path);

/// <summary>An answer to send back on <paramref name="Path"/>, and what to log of it: an outcome and a count.</summary>
public sealed record DashAnswer(string Path, DashMessage Reply, DashOutcome Outcome, int Count) : DashTurn(Path);

/// <summary>A send to carry out by the phone's own road (<see cref="TextCascade"/>), then answer (<see cref="DashInbox.Delivered"/>).</summary>
public sealed record DashSendTurn(DashSend Send, DashSendPlan Plan) : DashTurn(WatchIdentity.DashSendPath);

/// <summary>A file or a delete the relay planned: file each message, or hold each in the delete buffer for the window.</summary>
public sealed record DashActTurn(string Path, string Act, DashActKind Kind, DashActPlan Plan) : DashTurn(Path);

/// <summary>An undo: unfile what a filing took, or let what a delete held go from the buffer.</summary>
public sealed record DashUndoTurn(DashUndoPlan Plan) : DashTurn(WatchIdentity.DashUndoPath);

/// <summary>A read: stamp the thread read in the store the Mailbox reads.</summary>
public sealed record DashReadTurn(DashReadPlan Plan) : DashTurn(WatchIdentity.DashReadPath);

/// <summary>A call note: keep it where Post keeps call notes.</summary>
public sealed record DashNoteTurn(DashNotePlan Plan) : DashTurn(WatchIdentity.DashCallNotePath);

/// <summary>
/// Nothing to do: an ask for a correspondence Post no longer shows (<see cref="DashOutcome.Unknown"/>), or the watch's
/// half of a contract arriving at Post (no outcome), which only a watch reads.
/// </summary>
public sealed record DashNoTurn(string Path, DashOutcome? Outcome) : DashTurn(Path);

/// <summary>
/// Post's listener's decisions (P14 B2), pure: each message from the wrist becomes an answer from the Mailbox's own view,
/// or one of <see cref="DashRelay"/>'s plans for the head to carry out through Post's own code. The Mailbox is read only
/// for a message that needs it (an undo needs only the book). A send is put in the book before it is carried out and
/// answered from the book if it is heard again, so a duplicated send never goes twice; the head serialises messages, so
/// the book's look and its write are one step.
/// </summary>
public static class DashInbox
{
    public static DashTurn Turn(DashMessage message, Func<DashRelay> relay, DashActs acts, DashConditions conditions)
    {
        switch (message)
        {
            case DashPeopleAsk:
                var people = relay().People();
                return new DashAnswer(message.Path, people, DashOutcome.Answered, people.People.Count);
            case DashThreadAsk ask:
                return relay().Thread(ask.Key) is { } thread
                    ? new DashAnswer(message.Path, thread, DashOutcome.Answered, thread.Messages.Count)
                    : new DashNoTurn(message.Path, DashOutcome.Unknown);
            case DashMailAsk:
                var mail = relay().Mail();
                return new DashAnswer(message.Path, mail, DashOutcome.Answered, mail.Mail.Count);
            case DashSend send:
                return Send(send, relay, acts, conditions);
            case DashFile file:
                return new DashActTurn(message.Path, file.Id, DashActKind.File, relay().File(file, conditions.Now, conditions.PhoneWindow, acts));
            case DashDelete delete:
                return new DashActTurn(message.Path, delete.Id, DashActKind.Delete, relay().Delete(delete, conditions.Now, conditions.PhoneWindow, acts));
            case DashUndo undo:
                return new DashUndoTurn(DashRelay.Undo(undo, conditions.Now, acts));
            case DashRead read:
                return new DashReadTurn(relay().Read(read));
            case DashCallNote note:
                return new DashNoteTurn(relay().CallNote(note, conditions.Now));
            default:
                // People waiting, a conversation, a send's answer or mail lines: Post's own answers, read only on the wrist.
                return new DashNoTurn(message.Path, null);
        }
    }

    /// <summary>The send was carried out: its outcome goes in the book and is the answer, now and if it is heard again.</summary>
    public static DashSent Delivered(DashSend send, TextDelivery delivery, DashActs acts)
    {
        var outcome = OutcomeOf(delivery);
        acts.Sent(send.Id, outcome);
        return new DashSent(send.Id, outcome);
    }

    /// <summary>
    /// What the wrist is told: sent when a route took it (an MMS once the platform has it, as the phone's own send);
    /// no route when nothing could carry it and the bridge was not even up; failed when the route the phone chose
    /// refused it.
    /// </summary>
    public static DashSendOutcome OutcomeOf(TextDelivery delivery) =>
        delivery.Went ? DashSendOutcome.Sent
        : delivery.Path == TextSendPath.Unavailable && !delivery.BridgeTried ? DashSendOutcome.NoRoute
        : DashSendOutcome.Failed;

    private static DashTurn Send(DashSend send, Func<DashRelay> relay, DashActs acts, DashConditions conditions)
    {
        if (acts.Knows(send.Id))
        {
            // Heard again: answered as it went (or failed, if it never said), and never sent a second time.
            return new DashAnswer(send.Path, new DashSent(send.Id, acts.SendAnswer(send.Id) ?? DashSendOutcome.Failed), DashOutcome.Again, 0);
        }

        var plan = relay().Send(send, conditions.BridgeConnected, conditions.CanSendSms);
        acts.Sending(send.Id, conditions.Now);
        if (plan.Refused is { } refused)
        {
            acts.Sent(send.Id, refused);
            return new DashAnswer(send.Path, new DashSent(send.Id, refused), refused == DashSendOutcome.Nowhere ? DashOutcome.Nowhere : DashOutcome.NoRoute, 0);
        }
        return new DashSendTurn(send, plan);
    }
}
