using System.Text.Json;

namespace Codex.Domain.Post;

/// <summary>One event from the Messages-for-Web bridge.</summary>
public abstract record BridgeEvent;

/// <summary>An attachment as the bridge describes it; <see cref="Key"/> is the base64 decryption key.</summary>
public sealed record BridgeMedia(string MediaId, string Key, string MimeType, string? Name, long Size);

/// <summary>
/// One message. <see cref="Timestamp"/> is libgm's microseconds (read with
/// <see cref="BridgeTimestamp"/>); <see cref="IsOld"/> is history the bridge replays rather
/// than a message that has just arrived.
/// </summary>
public sealed record BridgeMessage(
    string MessageId,
    string ConversationId,
    string ParticipantId,
    bool FromMe,
    string Text,
    long Timestamp,
    bool IsOld,
    bool Failed,
    IReadOnlyList<BridgeMedia> Media) : BridgeEvent;

public sealed record BridgeParticipant(string Id, string Number, string Name, bool IsMe);

public sealed record BridgeConversation(
    string ConversationId,
    string Name,
    bool IsGroup,
    IReadOnlyList<BridgeParticipant> Participants) : BridgeEvent
{
    /// <summary>When the conversation last moved, in libgm's microseconds.</summary>
    public long LastTimestamp { get; init; }
}

/// <summary>Pairing finished; <see cref="Session"/> is the blob to keep (and back up to the vault).</summary>
public sealed record BridgePaired(string Session) : BridgeEvent;

public sealed record BridgePairFailed(PairingFailure Reason, string Error) : BridgeEvent;

/// <summary>The session's tokens moved on; a session saved before this will not sign in.</summary>
public sealed record BridgeSessionChanged(string Session) : BridgeEvent;

/// <summary>The pairing is gone (unpaired in Messages, signed out, refused). Only pairing again helps.</summary>
public sealed record BridgeLoggedOut(string Reason) : BridgeEvent;

public enum BridgeConnectionState
{
    Unknown,
    Ready,
    ListenError,
    Recovered,
    PhoneNotResponding,
    PhoneResponding,
    /// <summary>Another Messages-for-Web session (a browser) became the active one.</summary>
    Inactive,
    Active,
    Fatal,
}

public sealed record BridgeConnection(BridgeConnectionState State, string? Error) : BridgeEvent;

/// <summary>
/// The C# half of the bridge's JSON contract (gmbridge/bridge.go writes the other half).
/// Anything unknown, garbled or missing its identifying fields reads as null rather than
/// throwing: a newer bridge can add an event, and one bad document must not stop the stream.
/// </summary>
public static class BridgeEvents
{
    public static BridgeEvent? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<BridgeConversation> ReadConversations(string json) =>
        ReadList(json).OfType<BridgeConversation>().ToList();

    public static IReadOnlyList<BridgeMessage> ReadMessages(string json) =>
        ReadList(json).OfType<BridgeMessage>().ToList();

    private static List<BridgeEvent> ReadList(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            return document.RootElement.EnumerateArray()
                .Select(Read)
                .OfType<BridgeEvent>()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static BridgeEvent? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return Text(root, "type") switch
        {
            "message" => Message(root),
            "conversation" => Conversation(root),
            "paired" => Text(root, "session") is { Length: > 0 } session ? new BridgePaired(session) : null,
            "pairFailed" => new BridgePairFailed(Reason(Text(root, "reason")), Text(root, "error") ?? string.Empty),
            "sessionChanged" => Text(root, "session") is { Length: > 0 } session ? new BridgeSessionChanged(session) : null,
            "loggedOut" => new BridgeLoggedOut(Text(root, "reason") ?? string.Empty),
            "connection" => new BridgeConnection(State(Text(root, "state")), Text(root, "error")),
            _ => null,
        };
    }

    private static BridgeMessage? Message(JsonElement root)
    {
        var messageId = Text(root, "messageId");
        var conversationId = Text(root, "conversationId");
        if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(conversationId))
        {
            return null;
        }
        var media = new List<BridgeMedia>();
        if (root.TryGetProperty("media", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                var mediaId = Text(entry, "mediaId");
                if (string.IsNullOrEmpty(mediaId))
                {
                    continue;
                }
                media.Add(new BridgeMedia(
                    mediaId,
                    Text(entry, "key") ?? string.Empty,
                    Text(entry, "mime") is { Length: > 0 } mime ? mime : "application/octet-stream",
                    Text(entry, "name"),
                    Number(entry, "size")));
            }
        }
        return new BridgeMessage(
            messageId,
            conversationId,
            Text(root, "participantId") ?? string.Empty,
            Flag(root, "fromMe"),
            Text(root, "text") ?? string.Empty,
            Number(root, "timestamp"),
            Flag(root, "isOld"),
            Flag(root, "failed"),
            media);
    }

    private static BridgeConversation? Conversation(JsonElement root)
    {
        var conversationId = Text(root, "conversationId");
        if (string.IsNullOrEmpty(conversationId))
        {
            return null;
        }
        var participants = new List<BridgeParticipant>();
        if (root.TryGetProperty("participants", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                participants.Add(new BridgeParticipant(
                    Text(entry, "id") ?? string.Empty,
                    Text(entry, "number") ?? string.Empty,
                    Text(entry, "name") ?? string.Empty,
                    Flag(entry, "isMe")));
            }
        }
        return new BridgeConversation(conversationId, Text(root, "name") ?? string.Empty, Flag(root, "isGroup"), participants)
        {
            LastTimestamp = Number(root, "timestamp"),
        };
    }

    private static PairingFailure Reason(string? reason) => reason switch
    {
        "wrongEmoji" => PairingFailure.WrongEmoji,
        "cancelled" => PairingFailure.Cancelled,
        "timeout" => PairingFailure.TimedOut,
        "noDevices" => PairingFailure.NoPhone,
        "phoneNotResponding" => PairingFailure.PhoneNotResponding,
        "signedOut" => PairingFailure.SignedOut,
        _ => PairingFailure.Other,
    };

    private static BridgeConnectionState State(string? state) => state switch
    {
        "ready" => BridgeConnectionState.Ready,
        "listenError" => BridgeConnectionState.ListenError,
        "recovered" => BridgeConnectionState.Recovered,
        "phoneNotResponding" => BridgeConnectionState.PhoneNotResponding,
        "phoneResponding" => BridgeConnectionState.PhoneResponding,
        "inactive" => BridgeConnectionState.Inactive,
        "active" => BridgeConnectionState.Active,
        "fatal" => BridgeConnectionState.Fatal,
        _ => BridgeConnectionState.Unknown,
    };

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : 0;
}

/// <summary>
/// Which conversations a sync has to read. Events carry everything new while the bridge is
/// connected; a sync covers what arrived while it was not, so it reads only conversations that
/// moved after the last one it finished.
/// </summary>
public static class BridgeSync
{
    public sealed record Plan(IReadOnlyList<BridgeConversation> Conversations, long Through);

    public static Plan Moved(IReadOnlyList<BridgeConversation> listed, long syncedThrough)
    {
        var moved = listed.Where(c => c.LastTimestamp > syncedThrough).ToList();
        var through = moved.Count == 0 ? syncedThrough : Math.Max(syncedThrough, moved.Max(c => c.LastTimestamp));
        return new Plan(moved, through);
    }
}
