namespace Codex.Domain.Post;

/// <summary>How a text can be got rid of, best first.</summary>
public enum TextDeletePath
{
    /// <summary>Delete the row from the system SMS provider. Only the default app may.</summary>
    SmsProvider,

    /// <summary>The same, for an MMS row.</summary>
    MmsProvider,

    /// <summary>Delete through the Messages-for-Web bridge, for a message that never reached the provider.</summary>
    Bridge,

    /// <summary>Stop showing it here. Weaker than the rest, and the caller has to know that.</summary>
    HideLocally,
}

/// <summary>How a text can be sent, best first.</summary>
public enum TextSendPath
{
    /// <summary>Through the bridge — the only route that carries RCS and full-quality pictures.</summary>
    Bridge,

    /// <summary>Post sends the SMS itself, as role holder.</summary>
    NativeSms,

    /// <summary>Hand the owner to the messaging app. The last resort.</summary>
    DeepLink,
}

/// <summary>
/// Which route a delete takes. Deleting for real is half of what this phase exists to fix,
/// and "for real" differs by where the message came from: a provider row can only be
/// deleted by the default SMS app, and a bridge message never reached the provider at all
/// because RCS does not.
///
/// Hiding is the honest floor rather than a silent failure — it is a distinct answer so
/// the screen can say the message is gone from Post but not from the phone.
/// </summary>
public static class TextDelete
{
    public static TextDeletePath Choose(string sourceId, bool holdsSmsRole, bool bridgeConnected)
    {
        if (sourceId.StartsWith("sms:", StringComparison.Ordinal))
        {
            return holdsSmsRole ? TextDeletePath.SmsProvider : TextDeletePath.HideLocally;
        }
        if (sourceId.StartsWith("mms:", StringComparison.Ordinal))
        {
            return holdsSmsRole ? TextDeletePath.MmsProvider : TextDeletePath.HideLocally;
        }
        if (sourceId.StartsWith("gm:", StringComparison.Ordinal))
        {
            return bridgeConnected ? TextDeletePath.Bridge : TextDeletePath.HideLocally;
        }
        return TextDeletePath.HideLocally;
    }
}

/// <summary>
/// Which route a send takes. The bridge leads because it is the only one that carries RCS
/// and pictures at full quality, but it can only reply into a conversation it already
/// knows — so a person the bridge has never seen falls through to Post sending the SMS
/// itself, which is what holding the role buys.
///
/// The deep link is last and is the thing the owner most wants gone: leaving Post to
/// answer someone is the failure this app is measured against. It stays only because a
/// text that cannot be sent at all is worse.
/// </summary>
public static class TextSend
{
    public static TextSendPath Choose(bool bridgeConnected, bool hasBridgeConversation, bool holdsSmsRole)
    {
        if (bridgeConnected && hasBridgeConversation)
        {
            return TextSendPath.Bridge;
        }
        return holdsSmsRole ? TextSendPath.NativeSms : TextSendPath.DeepLink;
    }
}
