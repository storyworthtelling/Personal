using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Codex.Ui.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Post;

/// <summary>
/// Post's app shell, and only the shell: the frame, the binder gutter, and which area is
/// showing. It holds no mail, contact or call state of its own — each <see cref="PostArea"/>
/// owns what it shows.
///
/// What is left here is the handful of things no area can own: the runtime permissions,
/// the background sync schedule, and the one-time restore of the mail credentials from
/// the encrypted Drive vault after a reset.
///
/// There is deliberately no voiced greeting under the header. The owner's voice gets
/// exactly one seat in Post — the empty Mailbox (<see cref="Codex.Domain.Post.EmptyMailbox"/>) —
/// and a greeting sitting above a list of other people's messages is the voice standing
/// next to content, which §14 rules out.
/// </summary>
[Activity(
    Label = "Post",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class PostActivity : Activity
{
    // One Google sign-in for the whole head, scoped for BOTH jobs it has to do: Drive, for
    // the encrypted secrets vault, and the two Gmail scopes the API path needs. Asking
    // twice would mean two consent screens and two tokens for one account.
    private readonly VoiceGoogleAccount voiceAccount = MailAccess.Account();

    private PostArea[] areas = [];
    private int activeIndex;

    private FrameLayout content = null!;
    private FrameLayout bottomBarSlot = null!;
    private BinderTabs binder = null!;
    private LinearLayout headerSlot = null!;

    private PostArea Active => areas[activeIndex];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);

        areas =
        [
            new MailboxArea(this),
            new FiledArea(this),
            new RolodexArea(this),
            new SettingsArea(this, voiceAccount),
        ];
        foreach (var area in areas)
        {
            var which = area;
            area.Changed += () => { if (Active == which) RenderContent(); };
        }

        RestoreGmailFromVault();
        SetContentView(BuildLayout());

        RequestRuntimePermissions();

        try
        {
            EmailSyncWorker.Schedule(this);
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("Post", $"Could not schedule email sync: {e}");
            PostDiagnostics.LogWarning("EmailSync", "Could not schedule email sync", e);
        }

        CrashReporter.ShowPendingCrash(this);
        RenderContent();
        CheckSmsRole();
    }

    // Post cannot send a text or delete one for real without the SMS role, and the role
    // can be taken away by something Post never sees — a policy pass, an OS update, the
    // cutover's reset. So it is re-read on every launch rather than assumed from the day
    // it was granted, and what comes back goes behind the fold: it is something waiting,
    // not something to interrupt the owner with (§13).
    private void CheckSmsRole()
    {
        var notice = SmsRoleAccess.CheckOnLaunch(this, new AccountStore(this));
        DogEar.SetNotice(this, content, NoticeKeys.SmsRole, notice.Speaks
            ? new DogEar.Notice(
                notice.Headline,
                notice.Detail,
                notice.WasLost ? PaperTheme.ProblemMark : PaperTheme.CautionMark,
                notice.CanRequest ? "Make Post the texting app" : null,
                notice.CanRequest ? () => SmsRoleAccess.Request(this) : null)
            : null);
    }

    // After a reinstall or the cutover's factory reset, refill the mail credentials from the
    // encrypted Drive vault (needs the vault passphrase set + Google signed in). Runs only when
    // nothing is configured locally, and syncs once if the restore completes the setup.
    // Best-effort and off the UI thread.
    private void RestoreGmailFromVault() => Task.Run(async () =>
    {
        try
        {
            var account = new AccountStore(this);
            if (account.IsConfigured)
            {
                return; // already have local credentials — never overwrite them
            }

            var access = new SecretsVaultAccess(voiceAccount.GetAccessToken, "Post");
            var result = await access.Service.LoadAsync(CancellationToken.None);
            if (result.Status != SecretsVaultStatus.Loaded)
            {
                return;
            }

            SharedDriveFileIds.SeedFrom(result.Vault.DriveFileIds);

            var gmail = result.Vault.Gmail;
            if (string.IsNullOrEmpty(account.GmailAddress) && !string.IsNullOrEmpty(gmail.Address))
            {
                account.GmailAddress = gmail.Address;
            }
            if (string.IsNullOrEmpty(account.GmailAppPassword) && !string.IsNullOrEmpty(gmail.AppPassword))
            {
                account.GmailAppPassword = gmail.AppPassword;
            }
            if (string.IsNullOrEmpty(account.GmBridgeSession) && !string.IsNullOrEmpty(gmail.BridgeSession))
            {
                account.GmBridgeSession = gmail.BridgeSession;
            }

            if (account.IsConfigured)
            {
                EmailSyncWorker.RunOnce(this);
            }

            // A device with nothing filed takes the archive back down: after the cutover's
            // factory reset the ghosts are the only copy of messages whose originals were
            // deleted at the source on purpose, so there is nothing else to rebuild from.
            await new ArchiveBackup(voiceAccount.GetAccessToken, "Post").RestoreIfEmptyAsync(this);
        }
        catch
        {
            // Ignore: restore is a convenience; Settings always accepts the credentials manually.
        }
    });

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);

        // The page, and the binder gutter down its right edge. Everything above the
        // bottom bar lives inside the page; the tabs sit outside it, which is what stops
        // them ever overlapping content (§9's reserved gutter).
        var spread = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        headerSlot = new LinearLayout(this) { Orientation = Orientation.Vertical };
        headerSlot.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);
        page.AddView(headerSlot);

        content = new FrameLayout(this);
        foreach (var area in areas)
        {
            area.Overlay = content; // dog-ears and action cards overlay the content frame
        }

        content.SetPadding(edge, 0, edge, 0);
        page.AddView(content, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        spread.AddView(page, new LinearLayout.LayoutParams(
            0, ViewGroup.LayoutParams.MatchParent) { Weight = 1 });

        binder = new BinderTabs(this);
        binder.SetAreas(areas.Select(area => area.Tab), activeIndex);
        binder.AreaSelected += index =>
        {
            // Tapping the tab you are already on returns that area to its root, which is
            // why this is not guarded on the index having changed.
            if (index == activeIndex)
            {
                while (Active.GoBack())
                {
                }
            }

            activeIndex = index;
            binder.SetActive(index);
            RenderContent();
        };
        spread.AddView(binder, new LinearLayout.LayoutParams(
            PaperTheme.Dp(this, BinderTabs.WidthDp), ViewGroup.LayoutParams.MatchParent));

        root.AddView(spread, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        // A slot rather than a bar: each area supplies its own actions, and an area with
        // none simply leaves it empty.
        bottomBarSlot = new FrameLayout(this);
        root.AddView(bottomBarSlot);
        return root;
    }

    private void RenderContent()
    {
        headerSlot.RemoveAllViews();
        headerSlot.AddView(PaperUi.ScreenHeader(this, Active.Title, Active.Tab.Hue));

        content.RemoveAllViews();
        content.AddView(Active.BuildView(), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        bottomBarSlot.RemoveAllViews();
        if (Active.BuildBottomBar() is { } bar)
        {
            bottomBarSlot.AddView(bar);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        foreach (var area in areas)
        {
            area.Resume();
        }
        RenderContent();
    }

    protected override void OnPause()
    {
        base.OnPause();
        foreach (var area in areas)
        {
            area.Pause();
        }
    }

    public override void OnBackPressed()
    {
        // The area gets first refusal — only it knows whether it is drilled into
        // something. It raises its own redraw if it used the press.
        if (!Active.GoBack())
        {
            base.OnBackPressed();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == SmsRoleAccess.RequestCode)
        {
            // Re-read rather than trusting the result code: the owner can leave the dialog
            // by any route, and the only answer that matters is what the platform says now.
            CheckSmsRole();
            return;
        }

        foreach (var area in areas)
        {
            area.OnActivityResult(requestCode, resultCode, data);
        }
    }

    private void RequestRuntimePermissions()
    {
        var wanted = new[]
        {
            Manifest.Permission.ReadContacts,
            Manifest.Permission.PostNotifications,
            // Placing calls from the Rolodex (ActionCall).
            Manifest.Permission.CallPhone,
            // READ_CALL_LOG and READ_SMS are hard-restricted: these requests auto-deny
            // unless the APK was installed whitelisted, but they take effect post-cutover.
            Manifest.Permission.ReadCallLog,
            Manifest.Permission.ReadSms,
            // Reading the day a message proposes, and writing the event once the owner
            // accepts it. Refused, a date is still detected and still offered — it simply
            // says the day was not checked.
            Manifest.Permission.ReadCalendar,
            Manifest.Permission.WriteCalendar,
        };
        var toRequest = wanted
            .Where(permission => CheckSelfPermission(permission) != Permission.Granted)
            .ToArray();
        if (toRequest.Length > 0)
        {
            RequestPermissions(toRequest, 0);
        }
    }

    public override void OnRequestPermissionsResult(
        int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        foreach (var area in areas)
        {
            area.OnPermissionsResult();
        }
    }
}
