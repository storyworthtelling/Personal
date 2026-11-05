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

    /// <summary>Post sends a plain SMS itself, through the phone.</summary>
    NativeSms,

    /// <summary>
    /// Nothing can carry it right now. The text stays in Post with the reason; the owner is
    /// never handed to another app to reply.
    /// </summary>
    Unavailable,
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
/// Which route a send takes. The bridge leads: it is Google Messages itself, so it carries
/// RCS, pictures at full quality and groups, and it can start a conversation from a number
/// as well as reply into one. With the bridge down, Post sends a plain SMS through the phone
/// (SEND_SMS, no role needed — Android files it for the texting app, so Messages shows it).
///
/// There is no third route out to Google Messages: every reply happens inside Post (the
/// owner's ruling). With neither route available the text stays in Post with the reason,
/// rather than handing the owner to another app.
/// </summary>
public static class TextSend
{
    public static TextSendPath Choose(bool bridgeConnected, bool canSendSms) =>
        bridgeConnected ? TextSendPath.Bridge
        : canSendSms ? TextSendPath.NativeSms
        : TextSendPath.Unavailable;

    /// <summary>What the owner is told when a text could not go by any route.</summary>
    public const string UnavailableReason =
        "Post could not reach Google Messages and has no permission to send a plain text. "
        + "Your words are still in the box; try again once the phone is online.";
}

/// <summary>
/// Where the Mailbox reads texts from. Once the bridge is paired, Google Messages is the record:
/// every SMS, MMS and RCS message comes through the bridge with its real conversation, so the
/// phone's own text store is not read as well — a plain SMS would otherwise arrive twice, once
/// from each. Unpaired, the store is the only full history Post can read.
/// </summary>
public static class TextSources
{
    public static bool ReadsProvider(bool bridgePaired) => !bridgePaired;
}
