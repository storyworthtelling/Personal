namespace Codex.Domain.Post;

/// <summary>
/// The routes a text can take on this phone, as the head reaches them: Google Messages over the bridge, a plain text
/// through the phone, and an MMS. The bridge's own calls never throw (they log and answer false or null); the phone's
/// two may.
/// </summary>
public interface ITextRoutes
{
    /// <summary>The bridge conversation with these numbers, found or started by Google Messages; null when there is none.</summary>
    string? FindOrStartConversation(IReadOnlyList<string> recipients);

    /// <summary>Whether the bridge took the words into the conversation.</summary>
    bool TrySend(string conversationId, string text);

    /// <summary>Whether the bridge took the picture or video into the conversation.</summary>
    bool TrySendMedia(string conversationId, MmsOutboundPart media);

    /// <summary>A plain text to one number, through the phone.</summary>
    void SendText(string number, string text);

    /// <summary>One MMS to everyone, never a text to each.</summary>
    void SendMms(IReadOnlyList<string> recipients, string? text, IReadOnlyList<MmsOutboundPart> media);
}

/// <summary>
/// How a send went: the route it took (or <see cref="TextSendPath.Unavailable"/> when none could carry it), whether the
/// bridge was tried first, and the failure, kept whole, when the route the phone chose refused it.
/// </summary>
public sealed record TextDelivery(TextSendPath Path, bool BridgeTried, Exception? Failure = null)
{
    /// <summary>A route took it.</summary>
    public bool Went => Failure is null && Path != TextSendPath.Unavailable;
}

/// <summary>
/// The phone's one send road (P14 B2, out of the Mailbox's send so the Mailbox and the wrist's relay take the same one):
/// Google Messages over the bridge first, into the conversation the correspondence last spoke in or one started for its
/// numbers; then, with the bridge down or refusing, what <see cref="TextSend.WithoutBridge"/> says: a plain text to a
/// person, one MMS to a group, or nothing and the reason. A picture goes by the bridge first and is otherwise an MMS, to
/// one person as to a group. Pure: the head reaches the routes and says what happened.
/// </summary>
public static class TextCascade
{
    public static TextDelivery Text(
        ITextRoutes routes, IReadOnlyList<string> recipients, string? conversationId, string text, bool bridgeConnected, bool canSendSms)
    {
        var bridgeTried = TextSend.Choose(bridgeConnected, canSendSms) == TextSendPath.Bridge;
        if (bridgeTried)
        {
            var target = conversationId ?? routes.FindOrStartConversation(recipients);
            if (target is not null && routes.TrySend(target, text))
            {
                return new TextDelivery(TextSendPath.Bridge, BridgeTried: true);
            }
        }

        // The bridge is down or refused it: a plain text through the phone, or one MMS to the group, if that can go.
        var path = TextSend.WithoutBridge(recipients.Count, canSendSms);
        if (path == TextSendPath.Unavailable)
        {
            return new TextDelivery(path, bridgeTried);
        }

        return Carry(path, bridgeTried, () =>
        {
            if (path == TextSendPath.GroupMms)
            {
                routes.SendMms(recipients, text, []);
            }
            else
            {
                routes.SendText(recipients[0], text);
            }
        });
    }

    public static TextDelivery Media(
        ITextRoutes routes, IReadOnlyList<string> recipients, string? conversationId, MmsOutboundPart media, bool bridgeConnected)
    {
        if (bridgeConnected)
        {
            var target = conversationId ?? routes.FindOrStartConversation(recipients);
            if (target is not null && routes.TrySendMedia(target, media))
            {
                return new TextDelivery(TextSendPath.Bridge, BridgeTried: true);
            }
        }

        // Without the bridge a picture is an MMS, to one person or to the whole group.
        return Carry(TextSendPath.GroupMms, bridgeConnected, () => routes.SendMms(recipients, null, [media]));
    }

    // The phone's own route, whose refusal is the answer: kept whole, stack and all, for the head to say and log.
    private static TextDelivery Carry(TextSendPath path, bool bridgeTried, Action send)
    {
        try
        {
            send();
            return new TextDelivery(path, bridgeTried);
        }
        catch (Exception e)
        {
            return new TextDelivery(path, bridgeTried, e);
        }
    }
}
