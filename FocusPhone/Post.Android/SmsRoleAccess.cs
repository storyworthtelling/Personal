using Storyworthtelling.Diagnostics;
using Codex.Ui;
using Codex.Domain.Diagnostics;
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

    public static SmsRoleStatus Status(Context host)
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

        // The role on the record (LOG-LOOP-PLAN.md §4.5): once per process, and again whenever it
        // changes — the check runs on every resume, and a line each time would bury the log.
        if (loggedStatus != status)
        {
            loggedStatus = status;
            PostDiagnostics.LogInfo("SmsRole", PostLogLines.SmsRole(status, notice));
        }

        // A request the owner made is answered by what the platform says now.
        if (Interlocked.Exchange(ref pendingRequest, null) is { } request)
        {
            if (status == SmsRoleStatus.Held)
            {
                request.Succeeded();
            }
            else
            {
                request.Failed($"the role is still {status} after the request");
            }
        }
        return notice;
    }

    private static SmsRoleStatus? loggedStatus;
    private static KeyAction? pendingRequest;

    public static void Request(Activity host)
    {
        var action = TapWatch.Action(PostKeyActions.RequestSmsRole, TimeSpan.FromMinutes(5));
        var roles = (RoleManager?)host.GetSystemService(Context.RoleService);
        if (roles is null || !roles.IsRoleAvailable(RoleManager.RoleSms!))
        {
            action.Failed("this device does not offer the SMS role");
            return;
        }
        try
        {
            host.StartActivityForResult(roles.CreateRequestRoleIntent(RoleManager.RoleSms!), RequestCode);
            pendingRequest = action;
        }
        catch (Exception e)
        {
            action.Failed($"the role request would not open: {e.GetType().Name}");
            PostDiagnostics.LogWarning("SmsRole", "The SMS role request would not open", e);
        }
    }
}
