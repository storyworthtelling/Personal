using Android.App;
using Android.App.Roles;
using Android.Content;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// The device side of the SMS role: what the platform says, and the one intent that asks
/// for it. What any of it MEANS to the owner is decided by <see cref="SmsRole"/>, which
/// is pure and tested; this file only reports and requests.
///
/// The role cannot be granted silently. The device owner has no API for it — there is no
/// <c>SetDefaultSmsApplication</c> in the bindings — so it costs exactly one consent
/// dialog, and on a locked-down phone that dialog may not appear at all. That is the
/// phase's largest open risk and it is settled on a device, not here.
/// </summary>
public static class SmsRoleAccess
{
    /// <summary>Arbitrary, distinct from the voice sign-in's code so results do not cross.</summary>
    public const int RequestCode = 4801;

    public static SmsRoleStatus Status(Activity host)
    {
        var roles = (RoleManager?)host.GetSystemService(Context.RoleService);
        if (roles is null || !roles.IsRoleAvailable(RoleManager.RoleSms!))
        {
            return SmsRoleStatus.Unavailable;
        }
        return roles.IsRoleHeld(RoleManager.RoleSms!) ? SmsRoleStatus.Held : SmsRoleStatus.Available;
    }

    /// <summary>
    /// Reads the role, records it for the next look, and returns what to tell the owner.
    /// Recording happens here rather than at the call site so the "did we just lose it"
    /// comparison cannot drift out of step with the reading it is compared against.
    /// </summary>
    public static SmsRoleNotice CheckOnLaunch(Activity host, AccountStore account)
    {
        var status = Status(host);
        var notice = SmsRole.Decide(status, account.SmsRoleHeldAtLastLaunch);
        account.SmsRoleHeldAtLastLaunch = SmsRole.Remember(status, account.SmsRoleHeldAtLastLaunch);
        return notice;
    }

    public static void Request(Activity host)
    {
        var roles = (RoleManager?)host.GetSystemService(Context.RoleService);
        if (roles is null || !roles.IsRoleAvailable(RoleManager.RoleSms!))
        {
            return;
        }
        host.StartActivityForResult(roles.CreateRequestRoleIntent(RoleManager.RoleSms!), RequestCode);
    }
}
