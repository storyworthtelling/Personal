using Android.Content;
using Codex.Domain.Post;
using Codex.Ui;
using Storyworthtelling.Diagnostics;

namespace Post;

/// <summary>
/// The key actions a send reports to: the text's own, and the one a group MMS starts when it takes over. A tap on the
/// phone is watched (O10); a send from the wrist is not a tap, so its reports go nowhere and the code reads the same.
/// </summary>
internal sealed record TextSending(KeyAction Text, Func<KeyAction> Group)
{
    // An MMS can sit on the network for a minute or more before the platform answers.
    private static readonly TimeSpan MmsWithin = TimeSpan.FromMinutes(3);

    public static TextSending FromTap() =>
        new(TapWatch.Action(PostKeyActions.SendSms, TimeSpan.FromSeconds(60)), FromTapGroup);

    public static TextSending FromWrist() =>
        new(KeyAction.Unwatched(PostKeyActions.SendSms), () => KeyAction.Unwatched(PostKeyActions.SendMms));

    /// <summary>A picture's MMS from a tap: the bridge's picture send has no key action of its own.</summary>
    public static KeyAction FromTapGroup() => TapWatch.Action(PostKeyActions.SendMms, MmsWithin);
}

/// <summary>
/// Post's one send road (P14 B2), taken out of the Mailbox so the Mailbox and Dash's listener send the same way: every
/// text, a reply, a first message or a group, goes by <see cref="TextCascade"/> (Google Messages over the bridge, then a
/// plain text to a person or one MMS to a group) and comes back as a <see cref="TextDelivery"/> for the caller to say in
/// its own place. The key actions and the failure lines are the Mailbox's, unchanged. Network and the platform's
/// telephony: call it off the main thread.
/// </summary>
internal sealed class TextCourier : ITextRoutes
{
    private readonly Context context;
    private readonly GmBridgeClient bridge;
    private readonly KeyAction sending;
    private readonly Func<KeyAction> groupSending;

    private TextCourier(Context context, KeyAction sending, Func<KeyAction> groupSending)
    {
        this.context = context;
        bridge = GmBridgeClient.Shared(context);
        this.sending = sending;
        this.groupSending = groupSending;
    }

    /// <summary>Whether Post may send a plain text through the phone (SEND_SMS, granted by Flyleaf's policy).</summary>
    public static bool CanSendSms(Context context) =>
        AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.SendSms)
            == Android.Content.PM.Permission.Granted;

    /// <summary>Sends <paramref name="text"/> to <paramref name="recipients"/> by the phone's one road.</summary>
    public static TextDelivery Deliver(
        Context context, IReadOnlyList<string> recipients, string? conversationId, string text, TextSending watch, bool canSendSms)
    {
        var courier = new TextCourier(context, watch.Text, watch.Group);
        var delivery = TextCascade.Text(courier, recipients, conversationId, text, courier.bridge.IsConnected, canSendSms);
        switch (delivery)
        {
            case { Path: TextSendPath.Bridge }:
                watch.Text.Succeeded();
                break;
            case { Path: TextSendPath.Unavailable }:
                watch.Text.Failed("no bridge and no SEND_SMS permission");
                break;
            case { Path: TextSendPath.GroupMms }:
                // The text's own action is done once the MMS takes over; the MMS reports to its own.
                watch.Text.Succeeded();
                break;
            case { Path: TextSendPath.NativeSms, Failure: { } failure }:
                watch.Text.Failed($"{failure.GetType().Name}: {failure.Message}");
                PostDiagnostics.LogWarning("Sms", "Sending a text failed", failure);
                break;
        }
        return delivery;
    }

    /// <summary>Sends a picture or a video: by the bridge first, otherwise as an MMS to one person or the whole group.</summary>
    public static TextDelivery DeliverMedia(
        Context context, IReadOnlyList<string> recipients, string? conversationId, MmsOutboundPart media, Func<KeyAction> groupSending)
    {
        var courier = new TextCourier(context, KeyAction.Unwatched(PostKeyActions.SendSms), groupSending);
        return TextCascade.Media(courier, recipients, conversationId, media, courier.bridge.IsConnected);
    }

    public string? FindOrStartConversation(IReadOnlyList<string> recipients) => bridge.FindOrStartConversation(recipients);

    public bool TrySend(string conversationId, string text) => bridge.TrySend(conversationId, text);

    public bool TrySendMedia(string conversationId, MmsOutboundPart media) =>
        bridge.TrySendMedia(conversationId, media.Data, media.FileName, media.MimeType);

    public void SendText(string number, string text) => SmsSender.Send(context, number, text, sending);

    public void SendMms(IReadOnlyList<string> recipients, string? text, IReadOnlyList<MmsOutboundPart> media)
    {
        var action = groupSending();
        try
        {
            MmsSender.Send(context, recipients, text, media, action);
        }
        catch (MmsAttachmentTooLargeException e)
        {
            action.Failed("the attachment is too large to send");
            PostDiagnostics.LogWarning("Mms", "Refused an oversized attachment", e);
            throw;
        }
        catch (Exception e)
        {
            action.Failed($"{e.GetType().Name}: {e.Message}");
            Android.Util.Log.Warn("Post", $"Group MMS send failed: {e}");
            PostDiagnostics.LogWarning("Mms", "Sending a group text failed", e);
            throw;
        }
    }
}
