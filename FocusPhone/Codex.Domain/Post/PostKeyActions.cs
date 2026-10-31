namespace Codex.Domain.Post;

/// <summary>
/// Post's key actions (LOG-LOOP-PLAN.md §4.1, O10): the taps that start background work, declared to the
/// tap watch so they are never judged dead and their failure or silence is logged. Wire names in the logs,
/// listed by <c>KeyActionsDeclaredTests</c>, so a new one is a deliberate edit.
/// </summary>
public static class PostKeyActions
{
    public const string SendSms = "post.send-sms";
    public const string SendMms = "post.send-mms";
    public const string File = "post.file";
    public const string Delete = "post.delete";
    public const string RequestSmsRole = "post.request-sms-role";
    public const string GmailSignIn = "post.gmail-sign-in";
    public const string OpenAttachment = "post.open-attachment";
    public const string SendLogsNow = "post.send-logs-now";

    public static IReadOnlyList<string> All { get; } =
        [SendSms, SendMms, File, Delete, RequestSmsRole, GmailSignIn, OpenAttachment, SendLogsNow];
}
