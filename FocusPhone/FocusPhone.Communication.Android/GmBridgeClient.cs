using System.Text.Json;
using Android.Content;
using FocusPhone.Domain.Timeline;

namespace FocusPhone.Communication;

/// <summary>
/// Wraps the gmbridge Messages-for-Web binding: pairs a Google account, keeps a
/// session, streams incoming texts into the timeline cache, and sends replies.
///
/// The bridge types come from FocusPhone.GmBridge.Binding, which only exists when
/// gmbridge.aar has been built (CI always builds it first). When it hasn't, the
/// GMBRIDGE constant is undefined and this compiles to a stub that reports
/// "unavailable" so texting cleanly falls back to the Google Messages deep link.
/// </summary>
public sealed class GmBridgeClient
{
    private static GmBridgeClient? shared;

    public static GmBridgeClient Shared(Context context) =>
        shared ??= new GmBridgeClient(context.ApplicationContext ?? context);

    private readonly Context context;

    private GmBridgeClient(Context context)
    {
        this.context = context;
    }

    /// <summary>True once a Google account has been paired (a session blob exists).</summary>
    public bool IsPaired => !string.IsNullOrEmpty(new AccountStore(context).GmBridgeSession);

#if GMBRIDGE
    private global::Gmbridge.Bridge? bridge;
    private bool connected;

    public bool IsConnected => connected;

    public void ConnectIfPaired()
    {
        if (connected)
        {
            return;
        }
        var session = new AccountStore(context).GmBridgeSession;
        if (string.IsNullOrEmpty(session))
        {
            return;
        }
        try
        {
            bridge = global::Gmbridge.Gmbridge.NewBridge(session, new EventProxy(this));
            bridge?.Connect();
            connected = true;
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Bridge connect failed: {e}");
        }
    }

    /// <summary>Begins Google-account pairing; returns the emoji to confirm in Google Messages.</summary>
    public string StartPairing(string cookiesJson)
    {
        bridge = global::Gmbridge.Gmbridge.NewBridge("", new EventProxy(this));
        return bridge!.StartGoogleAccountPairing(cookiesJson) ?? "";
    }

    /// <summary>Sends a reply to a known conversation. Returns false if the bridge isn't ready.</summary>
    public bool TrySend(string conversationId, string text)
    {
        if (!connected || bridge is null)
        {
            return false;
        }
        try
        {
            bridge.SendText(conversationId, text);
            return true;
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Bridge send failed: {e}");
            return false;
        }
    }

    private void OnEvent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "message":
                    CaptureIncoming(root);
                    break;
                case "paired":
                    SaveSession();
                    break;
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Bridge event parse failed: {e}");
        }
    }

    private void CaptureIncoming(JsonElement element)
    {
        var conversationId = element.TryGetProperty("conversationId", out var c) ? c.GetString() : null;
        var sender = element.TryGetProperty("sender", out var s) ? s.GetString() : null;
        var text = element.TryGetProperty("text", out var t) ? t.GetString() : null;
        var timestampMs = element.TryGetProperty("timestampMs", out var ts) && ts.TryGetInt64(out var value) ? value : 0;
        if (string.IsNullOrEmpty(sender))
        {
            return;
        }

        var item = new TimelineItem(
            TimelineItemKind.Message,
            ContactKeyNormalizer.NormalizeAny(sender),
            DateTimeOffset.FromUnixTimeMilliseconds(timestampMs),
            text,
            Incoming: true,
            SourceId: $"gm:{conversationId}:{timestampMs}")
        {
            ExternalConversationId = conversationId,
            Unread = true,
        };
        try
        {
            using var database = TimelineDatabase.Open(context);
            database.Cache.Upsert([item]);
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Bridge upsert failed: {e}");
        }
    }

    private void SaveSession()
    {
        try
        {
            var session = bridge?.ExportSession();
            if (!string.IsNullOrEmpty(session))
            {
                new AccountStore(context).GmBridgeSession = session;
            }
            connected = true;
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Bridge session export failed: {e}");
        }
    }

    // The Java-facing callback; forwards every bridge event JSON to OnEvent.
    private sealed class EventProxy : Java.Lang.Object, global::Gmbridge.IEventHandler
    {
        private readonly GmBridgeClient owner;

        public EventProxy(GmBridgeClient owner)
        {
            this.owner = owner;
        }

        public void OnEvent(string? eventJson)
        {
            if (eventJson is not null)
            {
                owner.OnEvent(eventJson);
            }
        }
    }
#else
    public bool IsConnected => false;

    public void ConnectIfPaired()
    {
    }

    public string StartPairing(string cookiesJson) => "";

    public bool TrySend(string conversationId, string text) => false;
#endif
}
