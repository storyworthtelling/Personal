using System.Collections.Concurrent;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Pulp.Domain.Sessions;

// Flyleaf's playback-session relay (SessionHandOff). Flyleaf holds this permission to send; declaring
// it here at signature level is what makes the receiver's Permission enforceable at all, since a
// permission nobody declares is never granted to anyone.
[assembly: Permission(Name = SessionHandOff.Permission, ProtectionLevel = Protection.Signature)]

namespace Pulp.Sessions;

/// <summary>
/// What the composition hands the receiver (set once at launch, in <c>PulpServices</c>): where a read
/// session goes to be matched with the title the household started and recorded as watch evidence.
/// Null until composed, in which case a session is read and dropped. The hook receives the meaning
/// only; the <see cref="Pulp.Domain.Watching.Watched"/> rule, not this layer, judges it.
/// </summary>
public static class SessionHooks
{
    public static Action<SessionMeaning>? Record { get; set; }
}

/// <summary>
/// Receives a playback session Flyleaf relays (finding 2.3, D30, D182, D288). Unreadable text is
/// ignored. Each readable one is interpreted against the previous message from the same app, so a
/// changed title is recognised, and handed to <see cref="SessionHooks.Record"/>.
/// </summary>
[BroadcastReceiver(
    Name = SessionHandOff.Receiver,
    Exported = true,
    Enabled = true,
    Permission = SessionHandOff.Permission)]
[IntentFilter([SessionHandOff.Action])]
public class SessionReceiver : BroadcastReceiver
{
    private static readonly ConcurrentDictionary<string, SessionMessage> Previous = new(StringComparer.Ordinal);

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != SessionHandOff.Action)
        {
            return;
        }

        var text = intent.GetStringExtra(SessionHandOff.SessionExtra);
        if (!SessionDecoder.TryDecode(text, out var message))
        {
            Android.Util.Log.Warn("Pulp", "A session broadcast carried no readable message; it was ignored.");
            return;
        }

        Previous.TryGetValue(message.Package, out var before);
        var meaning = SessionReading.Interpret(message, before, DateTimeOffset.Now);

        // A closed session starts the next one from nothing.
        if (message.State == SessionPlayState.Stopped)
        {
            Previous.TryRemove(message.Package, out _);
        }
        else
        {
            Previous[message.Package] = message;
        }

        try
        {
            SessionHooks.Record?.Invoke(meaning);
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("Pulp", $"Recording a session failed: {e}");
        }
    }
}
