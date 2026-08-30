namespace Post;

/// <summary>
/// Who owns which notice behind the fold. Several parts of a screen raise them
/// independently — the shell watches the SMS role, the Mailbox watches the sync — and each
/// one sets and clears only its own, so neither can silently erase the other's.
/// </summary>
internal static class NoticeKeys
{
    public const string SmsRole = "sms-role";
    public const string Sync = "sync";
    public const string Block = "block";
}
