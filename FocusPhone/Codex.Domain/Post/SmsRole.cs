namespace Codex.Domain.Post;

/// <summary>Where the SMS role stands on this device, as the platform reports it.</summary>
public enum SmsRoleStatus
{
    /// <summary>Post is the texting app. Sending and real deletion work.</summary>
    Held,

    /// <summary>The device offers the role and Post does not hold it. One consent dialog away.</summary>
    Available,

    /// <summary>The device does not offer the role at all — nothing to ask for.</summary>
    Unavailable,
}

/// <summary>Which app holds the texting (default SMS) role.</summary>
public enum TextingAppHolder
{
    GoogleMessages,
    Post,
    Other,
    Nobody,
}

/// <summary>
/// What to tell the owner about the texting role. Never a toast and never a banner on the
/// page: this goes behind the dog-ear like every other notice (§13).
/// <see cref="OffersMessages"/> is whether the notice carries a button that opens Google
/// Messages, which asks for the role itself.
/// </summary>
public sealed record SmsRoleNotice(bool Speaks, bool OffersMessages, string Headline, string Detail)
{
    public static SmsRoleNotice Silent { get; } = new(false, false, string.Empty, string.Empty);
}

/// <summary>
/// The launch check behind the texting role. Google Messages is meant to hold it: Messages
/// keeps RCS ("chat features") on only while it is the default SMS app, and Post reads and
/// sends through Messages over the Messages-for-Web bridge. Post held the role under the old
/// design, so a phone upgraded from it still has Post as the texting app — and RCS off —
/// until the role goes back.
///
/// <para>Post cannot hand the role over itself: only the app that wants it can ask. So the
/// notice opens Messages, which asks to be the default as soon as it is opened while it is not.
/// Re-checked on every launch, since a policy pass or an OS update can move it.</para>
///
/// <para>The wording is static and neutral. Post's only voiced surface is the empty Mailbox.</para>
/// </summary>
public static class SmsRole
{
    public const string GoogleMessagesPackage = "com.google.android.apps.messaging";

    /// <summary>Who holds the role, from the platform's default SMS package.</summary>
    public static TextingAppHolder HolderOf(string? defaultSmsPackage, string selfPackage) =>
        string.IsNullOrEmpty(defaultSmsPackage) ? TextingAppHolder.Nobody
        : defaultSmsPackage == GoogleMessagesPackage ? TextingAppHolder.GoogleMessages
        : defaultSmsPackage == selfPackage ? TextingAppHolder.Post
        : TextingAppHolder.Other;

    public static SmsRoleNotice Decide(TextingAppHolder holder, bool messagesInstalled)
    {
        if (holder == TextingAppHolder.GoogleMessages)
        {
            return SmsRoleNotice.Silent;
        }
        if (!messagesInstalled)
        {
            return new(true, false,
                "Google Messages is not installed.",
                "Post sends and receives texts through Google Messages. Install it from the Play Store, "
                + "then open Post again.");
        }
        return holder == TextingAppHolder.Post
            ? new(true, true,
                "Post still has texting.",
                "Google Messages needs to be the texting app to keep RCS on, and Post now works through it. "
                + "Open Messages and let it become the default.")
            : new(true, true,
                "Google Messages is not your texting app.",
                "Post works through Google Messages, and Messages keeps RCS on only as the texting app. "
                + "Open Messages and let it become the default.");
    }
}
