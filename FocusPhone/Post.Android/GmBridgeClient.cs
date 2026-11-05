using System.Text.Json;
using Android.Content;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Ui.Voice;

namespace Post;

/// <summary>
/// Post's side of the Messages-for-Web bridge. Google Messages holds the texting role (so RCS
/// stays on) and Post is a Messages-for-Web client of it on the same phone: pairing, the
/// session, every incoming and outgoing message, history, sending and deleting all go
/// through here.
///
/// <para><b>Staying connected without a notification of its own.</b> The bridge listens only
/// while Post's process is alive. Android keeps it alive while the notification listener is
/// bound, and every Google Messages notification reaches that listener, which calls
/// <see cref="ConnectIfPaired"/> — so a message that arrives while the bridge was down is
/// the thing that brings it back, and the bridge then delivers it with its real sender,
/// group and pictures. The 30-minute sync does the same. No foreground service, no
/// always-on notification (which the lockdown's disabled status bar would hide anyway).</para>
///
/// <para>Every bridge call is network work, so none of it runs on the UI thread: callers use
/// the async members.</para>
///
/// <para>The bridge types come from Codex.GmBridge.Binding, which only exists when
/// gmbridge.aar has been built (CI always builds it first). When it hasn't, GMBRIDGE is
/// undefined and this compiles to a stub that reports "not paired, not connected", so
/// texting falls back to plain SMS and the Google Messages deep link.</para>
/// </summary>
public sealed class GmBridgeClient
{
    private static GmBridgeClient? shared;

    public static GmBridgeClient Shared(Context context) =>
        shared ??= new GmBridgeClient(context.ApplicationContext ?? context);

    private const string Source = "Bridge";
    private const int SyncConversations = 25;
    private const long SyncMessagesPerConversation = 20;

    private readonly Context context;
    // Two locks: the conversation map is touched from bridge callbacks, which can arrive while
    // a connect is still in progress on another thread, so it never waits on the connection lock.
    private readonly object gate = new();
    private readonly object connection = new();
    private readonly Dictionary<string, BridgeConversation> conversations = new(StringComparer.Ordinal);

    private GmBridgeClient(Context context)
    {
        this.context = context;
    }

    /// <summary>Raised (on a background thread) for every pairing outcome.</summary>
    public event Action<BridgeEvent>? PairingChanged;

    /// <summary>True once a Google account has been paired (a session exists).</summary>
    public bool IsPaired => !string.IsNullOrEmpty(new AccountStore(context).GmBridgeSession);

    /// <summary>The last connection state the bridge reported, for Settings and the diagnostics export.</summary>
    public BridgeConnectionState LastState { get; private set; } = BridgeConnectionState.Unknown;

    /// <summary>True while a pairing waits for the owner to pick the emoji in Google Messages.</summary>
    public bool IsPairing { get; private set; }

    private void Handle(string json)
    {
        switch (BridgeEvents.Read(json))
        {
            case BridgeMessage message:
                Capture([message]);
                break;
            case BridgeConversation conversation:
                Remember(conversation);
                break;
            case BridgePaired paired:
                IsPairing = false;
                SaveSession(paired.Session);
                // A new pairing reads the recent history again from the start.
                new AccountStore(context).GmBridgeSyncedThrough = 0;
                PostDiagnostics.LogText(Source, "Paired with Google Messages");
                PairingChanged?.Invoke(paired);
                ReconnectFromSession();
                break;
            case BridgePairFailed failed:
                IsPairing = false;
                PostDiagnostics.LogWarning(Source, $"Pairing did not finish ({failed.Reason}): {failed.Error}");
                PairingChanged?.Invoke(failed);
                break;
            case BridgeSessionChanged changed:
                SaveSession(changed.Session);
                break;
            case BridgeLoggedOut loggedOut:
                // Only pairing again helps; keeping a dead session would retry it forever.
                PostDiagnostics.LogWarning(Source, "The pairing with Google Messages ended: " + loggedOut.Reason);
                Forget();
                new AccountStore(context).GmBridgeSession = null;
                break;
            case BridgeConnection connection:
                LastState = connection.State;
                PostDiagnostics.LogText(Source, $"Connection {connection.State}"
                    + (connection.Error is null ? string.Empty : ": " + connection.Error));
                if (connection.State == BridgeConnectionState.Ready)
                {
                    _ = SyncAsync();
                }
                break;
        }
    }

    private void Remember(BridgeConversation conversation)
    {
        lock (gate)
        {
            conversations[conversation.ConversationId] = conversation;
        }
    }

    private BridgeConversation? Known(string conversationId)
    {
        lock (gate)
        {
            return conversations.GetValueOrDefault(conversationId);
        }
    }

    private void Capture(IReadOnlyList<BridgeMessage> messages)
    {
        var items = messages
            .Select(message => BridgeMessageMapper.Map(message, Known(message.ConversationId)))
            .OfType<TimelineItem>()
            .ToList();
        foreach (var message in messages.Where(m => !m.IsOld && !m.FromMe))
        {
            PostDiagnostics.LogText(Source, "Bridge message; sent "
                + TextTrail.Lag(DateTimeOffset.UtcNow - BridgeTimestamp.ToInstant(message.Timestamp))
                + " before it reached Post");
        }
        if (items.Count == 0)
        {
            return;
        }
        try
        {
            using var database = TimelineDatabase.Open(context);
            database.Cache.Upsert(items);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, "Could not keep bridge messages", e);
        }
    }

    private void SaveSession(string session)
    {
        var account = new AccountStore(context);
        account.GmBridgeSession = session;
        _ = PublishSessionAsync(account);
    }

    // The session rides the shared vault beside the mail credentials, so a reset phone
    // restores it rather than pairing again. Best-effort: the next save carries it.
    private static async Task PublishSessionAsync(AccountStore account)
    {
        try
        {
            var access = new SecretsVaultAccess(MailAccess.Account().GetAccessToken, "Post");
            await access.Service.PublishAsync(vault => vault with
            {
                Gmail = vault.Gmail with { BridgeSession = account.GmBridgeSession ?? string.Empty },
            }, CancellationToken.None);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, "Could not back the bridge session up to the vault", e);
        }
    }

    // Brings the history in: the recent conversations, and the latest messages of every one
    // that has moved since the last sync. Events carry everything new while connected; this
    // covers what arrived while the bridge was down.
    public Task SyncAsync() => Task.Run(() =>
    {
        if (!IsConnected)
        {
            return;
        }
        try
        {
            var account = new AccountStore(context);
            var since = account.GmBridgeSyncedThrough;
            var listed = ListConversations(SyncConversations);
            foreach (var conversation in listed)
            {
                Remember(conversation);
            }
            var moved = BridgeSync.Moved(listed, since);
            foreach (var conversation in moved.Conversations)
            {
                Capture(FetchMessages(conversation.ConversationId, SyncMessagesPerConversation));
            }
            account.GmBridgeSyncedThrough = moved.Through;
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, "Bridge sync failed", e);
        }
    });

#if GMBRIDGE
    private global::Gmbridge.Bridge? bridge;

    public bool IsConnected => bridge?.IsConnected ?? false;

    /// <summary>Connects on the saved session, off the calling thread. Safe to call often.</summary>
    public void ConnectIfPaired() => Task.Run(() =>
    {
        lock (connection)
        {
            if (IsPairing || bridge?.IsConnected == true)
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
                bridge?.Disconnect();
                bridge = global::Gmbridge.Gmbridge.NewBridge(session, new EventProxy(this));
                bridge!.Connect();
                PostDiagnostics.LogText(Source, "Bridge connected");
            }
            catch (Exception e)
            {
                PostDiagnostics.LogWarning(Source, "Bridge connect failed", e);
            }
        }
    });

    /// <summary>
    /// Begins pairing with the Google account whose sign-in cookies are given (from Post's own
    /// sign-in page). Blocks on the network, so call it off the UI thread. Returns the emoji to
    /// pick in Google Messages; the outcome arrives through <see cref="PairingChanged"/>.
    /// </summary>
    public string StartPairing(string cookiesJson)
    {
        lock (connection)
        {
            bridge?.Disconnect();
            bridge = global::Gmbridge.Gmbridge.NewBridge(string.Empty, new EventProxy(this));
            IsPairing = true;
            try
            {
                return bridge!.StartGoogleAccountPairing(cookiesJson) ?? string.Empty;
            }
            catch
            {
                IsPairing = false;
                throw;
            }
        }
    }

    public void CancelPairing()
    {
        IsPairing = false;
        bridge?.CancelPairing();
    }

    private void ReconnectFromSession()
    {
        lock (connection)
        {
            bridge = null;
        }
        ConnectIfPaired();
    }

    private void Forget()
    {
        lock (connection)
        {
            bridge?.Disconnect();
            bridge = null;
        }
    }

    /// <summary>
    /// Takes the active Messages-for-Web session back when another browser took it. Called
    /// when Post comes to the front, never in the background, so a browser the owner is
    /// using elsewhere is not fought over.
    /// </summary>
    public void TakeOverIfInactive()
    {
        if (LastState != BridgeConnectionState.Inactive)
        {
            ConnectIfPaired();
            return;
        }
        Task.Run(() =>
        {
            try
            {
                bridge?.TakeOver();
                LastState = BridgeConnectionState.Active;
            }
            catch (Exception e)
            {
                PostDiagnostics.LogWarning(Source, "Could not take the Messages session back", e);
            }
        });
    }

    /// <summary>The conversation with these numbers, found or created by Messages. Network; off the UI thread.</summary>
    public string? FindOrStartConversation(IReadOnlyList<string> numbers)
    {
        if (!IsConnected || numbers.Count == 0)
        {
            return null;
        }
        try
        {
            var json = bridge!.StartConversation(JsonSerializer.Serialize(numbers), string.Empty);
            if (BridgeEvents.Read(json ?? string.Empty) is BridgeConversation conversation)
            {
                Remember(conversation);
                return conversation.ConversationId;
            }
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, $"Could not start a conversation with {numbers.Count} number(s)", e);
        }
        return null;
    }

    /// <summary>Sends text into a conversation. Network; off the UI thread. False if it did not go.</summary>
    public bool TrySend(string conversationId, string text)
    {
        if (!IsConnected)
        {
            return false;
        }
        try
        {
            bridge!.SendText(conversationId, text);
            return true;
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, "Bridge text send failed", e);
            return false;
        }
    }

    /// <summary>Sends a picture or file into a conversation. Network; off the UI thread.</summary>
    public bool TrySendMedia(string conversationId, byte[] data, string fileName, string mime)
    {
        if (!IsConnected)
        {
            return false;
        }
        try
        {
            bridge!.SendMedia(conversationId, data, fileName, mime);
            return true;
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, "Bridge media send failed", e);
            return false;
        }
    }

    /// <summary>Deletes a message in Google Messages (and so on the phone).</summary>
    public bool TryDelete(string messageId)
    {
        if (!IsConnected)
        {
            return false;
        }
        try
        {
            bridge!.DeleteMessage(messageId);
            return true;
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, "Bridge delete failed", e);
            return false;
        }
    }

    /// <summary>Downloads and decrypts an attachment. Network; off the UI thread.</summary>
    public byte[]? DownloadMedia(string mediaId, string key)
    {
        if (!IsConnected)
        {
            return null;
        }
        try
        {
            return bridge!.DownloadMedia(mediaId, key);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning(Source, "Bridge media download failed", e);
            return null;
        }
    }

    private IReadOnlyList<BridgeConversation> ListConversations(int count) =>
        BridgeEvents.ReadConversations(bridge?.ListConversations(count) ?? "[]");

    private IReadOnlyList<BridgeMessage> FetchMessages(string conversationId, long count) =>
        BridgeEvents.ReadMessages(bridge?.FetchMessages(conversationId, count) ?? "[]");

    // The Java-facing callback; forwards every bridge event JSON to Handle.
    private sealed class EventProxy : Java.Lang.Object, global::Gmbridge.IEventHandler
    {
        private readonly GmBridgeClient owner;

        public EventProxy(GmBridgeClient owner)
        {
            this.owner = owner;
        }

        public void OnEvent(string? eventJson)
        {
            if (eventJson is null)
            {
                return;
            }
            try
            {
                owner.Handle(eventJson);
            }
            catch (Exception e)
            {
                // An exception thrown back into Go would take the bridge down with it.
                PostDiagnostics.LogWarning(Source, "A bridge event could not be handled", e);
            }
        }
    }
#else
    public bool IsConnected => false;

    public void ConnectIfPaired()
    {
    }

    public string StartPairing(string cookiesJson) =>
        throw new InvalidOperationException("This build of Post has no Messages bridge (gmbridge.aar was not built).");

    public void CancelPairing()
    {
    }

    private void ReconnectFromSession()
    {
    }

    private void Forget()
    {
    }

    public void TakeOverIfInactive()
    {
    }

    public string? FindOrStartConversation(IReadOnlyList<string> numbers) => null;

    public bool TrySend(string conversationId, string text) => false;

    public bool TrySendMedia(string conversationId, byte[] data, string fileName, string mime) => false;

    public bool TryDelete(string messageId) => false;

    public byte[]? DownloadMedia(string mediaId, string key) => null;

    private IReadOnlyList<BridgeConversation> ListConversations(int count) => [];

    private IReadOnlyList<BridgeMessage> FetchMessages(string conversationId, long count) => [];
#endif
}
