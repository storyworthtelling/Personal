using Storyworthtelling.Diagnostics;
using Codex.Ui;
using Codex.Domain.Diagnostics;
using Android.App;
using Android.App.Roles;
using Android.Content;
using Codex.Domain.Post;

namespace Post;

/// <summary>
/// The device side of the texting role: who holds it, and the one way Post can help it move.
/// What any of it MEANS to the owner is decided by <see cref="SmsRole"/>, which is pure and
/// tested; this file only reports and opens.
///
/// <para>Google Messages is meant to hold the role (it keeps RCS on only as the default SMS app),
/// and Post works through it over the Messages-for-Web bridge. Post cannot give the role away —
/// only the app that wants it can ask — so the notice opens Messages, which asks to become the
/// default as soon as it is opened while it is not. A per-app block in Flyleaf only keeps
/// Messages off the shelf; it still runs and can be opened from here.</para>
///
/// <para>Post's role components stay declared (<see cref="SmsRoleComponents"/>), so the role can
/// still come back to Post if that is ever wanted again.</para>
/// </summary>
public static class SmsRoleAccess
{
    /// <summary>Whether Post itself holds the role (the provider accepts writes only from the holder).</summary>
    public static SmsRoleStatus Status(Context host)
    {
        var roles = (RoleManager?)host.GetSystemService(Context.RoleService);
        if (roles is null || !roles.IsRoleAvailable(RoleManager.RoleSms!))
        {
            return SmsRoleStatus.Unavailable;
        }
        return roles.IsRoleHeld(RoleManager.RoleSms!) ? SmsRoleStatus.Held : SmsRoleStatus.Available;
    }

    public static TextingAppHolder Holder(Context host) =>
        SmsRole.HolderOf(Android.Provider.Telephony.Sms.GetDefaultSmsPackage(host), host.PackageName ?? string.Empty);

    public static bool MessagesInstalled(Context host) =>
        host.PackageManager?.GetLaunchIntentForPackage(SmsRole.GoogleMessagesPackage) is not null;

    /// <summary>Reads who holds the role and returns what to tell the owner.</summary>
    public static SmsRoleNotice CheckOnLaunch(Activity host)
    {
        var holder = Holder(host);
        var notice = SmsRole.Decide(holder, MessagesInstalled(host));

        // The holder on the record (LOG-LOOP-PLAN.md §4.5): once per process, and again whenever it
        // changes — the check runs on every resume, and a line each time would bury the log.
        if (loggedHolder != holder)
        {
            loggedHolder = holder;
            PostDiagnostics.LogInfo("SmsRole", PostLogLines.SmsRole(holder, notice));
        }

        // An "open Messages" the owner tapped is answered by who holds the role now.
        if (holder == TextingAppHolder.GoogleMessages && Interlocked.Exchange(ref pendingRequest, null) is { } request)
        {
            request.Succeeded();
        }
        return notice;
    }

    private static TextingAppHolder? loggedHolder;
    private static KeyAction? pendingRequest;

    /// <summary>Opens Google Messages, which asks for the role itself.</summary>
    public static void OpenMessages(Activity host)
    {
        var action = TapWatch.Action(PostKeyActions.RequestSmsRole, TimeSpan.FromMinutes(5));
        var intent = host.PackageManager?.GetLaunchIntentForPackage(SmsRole.GoogleMessagesPackage);
        if (intent is null)
        {
            action.Failed("Google Messages is not installed");
            return;
        }
        try
        {
            host.StartActivity(intent);
            pendingRequest = action;
        }
        catch (Exception e)
        {
            action.Failed($"Google Messages would not open: {e.GetType().Name}");
            PostDiagnostics.LogWarning("SmsRole", "Google Messages would not open", e);
        }
    }
}
