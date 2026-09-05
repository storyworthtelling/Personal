using Android.Content;
using Codex.Infrastructure.Email;
using Codex.Ui.Voice;

namespace Post;

/// <summary>
/// Builds the Gmail API client for this head, in one place so the token, the HTTP client
/// and the health sink are wired identically wherever mail is reached — the worker, the
/// Sync action, a send, a delete.
///
/// The Google account here asks for the two mail scopes on top of Drive:
/// <c>gmail.modify</c> (read and trash) and <c>gmail.send</c>. Full <c>mail.google.com</c>
/// would be one scope instead of two but also grants permanent deletion, which Post never
/// wants — filing is a recoverable trash on purpose.
/// </summary>
internal static class MailAccess
{
    public const string ModifyScope = "https://www.googleapis.com/auth/gmail.modify";
    public const string SendScope = "https://www.googleapis.com/auth/gmail.send";

    /// <summary>The account, scoped for mail. Sign-in and token caching live inside it.</summary>
    public static VoiceGoogleAccount Account() => new(ModifyScope, SendScope);

    public static GmailApiClient Api(Context context, AccountStore account) =>
        new(account.GmailAddress ?? VoiceGoogleAccount.Email,
            Account().GetAccessToken,
            Http,
            new CredentialHealthStore(),
            // Only the MIME level knows whether a message had a plain-text part; by the
            // time it is a timeline item its body is already the readable form. This is the
            // number that says whether the shared web view is urgent or theoretical.
            observeBodyShape: (hasText, hasHtml) =>
                FieldNotesStore.Record(context, notes => notes.WithMail(hasText, hasHtml, [])));

    /// <summary>
    /// One attachment's bytes, on demand. Only the API path can fetch one: the IMAP client
    /// was never given the job, and it is the path being retired.
    /// </summary>
    public static byte[]? Attachment(
        Context context, Codex.Domain.Timeline.TimelineItem item,
        Codex.Domain.Timeline.AttachmentRef attachment)
    {
        var account = new AccountStore(context);
        if (Codex.Domain.Post.MailRoute.Choose(VoiceGoogleAccount.IsSignedIn, account.IsConfigured)
            != Codex.Domain.Post.MailTransport.GmailApi
            || item.MessageId is null
            || attachment.FileName is null)
        {
            return null;
        }

        return Api(context, account)
            .FetchAttachmentAsync(item.MessageId, attachment.FileName)
            .GetAwaiter().GetResult();
    }

    // One HttpClient for the process, per the usual guidance: a new one per call leaks
    // sockets, and mail is reached often enough for that to matter on a phone.
    private static readonly HttpClient Http = new();

    /// <summary>The same client, for anything else in the head that talks HTTP.</summary>
    public static HttpClient Shared => Http;
}
