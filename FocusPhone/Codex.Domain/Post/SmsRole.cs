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

/// <summary>
/// What to tell the owner about the SMS role. Never a toast and never a banner on the
/// page: this goes behind the dog-ear like every other notice (§13).
/// </summary>
public sealed record SmsRoleNotice(bool Speaks, bool CanRequest, bool WasLost, string Headline, string Detail)
{
    public static SmsRoleNotice Silent { get; } = new(false, false, false, string.Empty, string.Empty);
}

/// <summary>
/// The launch check behind the SMS role. Post cannot send a text or delete one for real
/// without it, and the role can be taken away with nothing to show for it — a policy
/// pass, an OS update, the cutover's reset. So it is re-checked on every launch rather
/// than assumed from the day it was granted.
///
/// The wording is static and neutral. Post's only voiced surface is the empty Mailbox;
/// a wry line about a lost role would be delivered as a <i>reward</i> for turning the
/// corner, which is the failure mode the fold exists to avoid.
/// </summary>
public static class SmsRole
{
    public static SmsRoleNotice Decide(SmsRoleStatus status, bool heldAtLastLaunch) => status switch
    {
        // A working app announcing that it works is the definition of noise, and that
        // holds whether or not it was working yesterday.
        SmsRoleStatus.Held => SmsRoleNotice.Silent,

        // Something took the role away and texting has been broken since. This must not
        // read like first-run setup, because the owner already did that.
        SmsRoleStatus.Available when heldAtLastLaunch => new(
            Speaks: true, CanRequest: true, WasLost: true,
            "Post is no longer your texting app.",
            "Something else took over. Texts you send from here will not go out, and deleting one "
            + "will not remove it from the phone, until Post has the role back."),

        SmsRoleStatus.Available => new(
            Speaks: true, CanRequest: true, WasLost: false,
            "Post is not your texting app yet.",
            "Until it is, Post can show your texts but cannot send one or delete one for real."),

        // Asking is not an option here, so no button is offered: one that opens nothing
        // is worse than plainly saying what has to happen first.
        _ => new(
            Speaks: true, CanRequest: false, WasLost: heldAtLastLaunch,
            "This phone is not offering the texting role.",
            "The device-owner lockdown suppresses the consent dialog. Relax the policy from Flyleaf's "
            + "admin mode, take the role, then put the policy back."),
    };
}
