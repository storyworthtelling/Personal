using Storyworthtelling.Diagnostics;
using Codex.Domain.Diagnostics;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Apps;
using Codex.Domain.Calls;
using Codex.Domain.Design;
using Codex.Domain.Policy;
using Codex.Ui;
using Storyworthtelling.Voice.Secrets;

namespace Flyleaf;

/// <summary>
/// The Flyleaf home: one continuous realistic bookcase (a scoped DESIGN.md §1/§5 exception).
/// The top is the mantel-clock shelf over the six cover-out home books; scrolling down reveals
/// the tray on the same shelf — the owner's sibling apps (cover-out), the category shelves
/// (spine-out, bronze bookends), and the loose apps. Tap a book to open it; long-press for the
/// Paper action card (Move to shelf / Feature on home / Block / Uninstall); long-press the clock
/// for Settings. A book with a notification flies a bookmark tab. Fully immersive — no bars.
/// </summary>
[Activity(
    Label = "Flyleaf",
    MainLauncher = true,
    Exported = true,
    LaunchMode = Android.Content.PM.LaunchMode.SingleTask,
    StateNotNeeded = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter(
    [Intent.ActionMain],
    Categories = [Intent.CategoryHome, Intent.CategoryDefault])]
public class HomeActivity : Activity
{
    private BookshelfView? shelf;
    private ShelfArt? art;
    private FrameLayout? root;
    private Button? setDefaultLauncherButton;
    private Button? setDefaultBrowserButton;
    private LinearLayout? callBar;
    private Button? callButton;
    private const int RequestBrowserRoleCode = 4200;

    // The Google account is used for the vault-backed update-token restore and the cover-load
    // voiced hello. Best-effort: both no-op until a Google account is signed in.
    private readonly Codex.Ui.Voice.VoiceGoogleAccount voiceAccount = new();
    private Codex.Ui.Voice.VoiceGreeting? greeting;

    // The cover-load overlay is a cold-start moment only — shown once per process, not on every
    // Home press (a launcher should return you to the shelf instantly).
    private static bool coverShownThisProcess;

    private TimeTickReceiver? timeTickReceiver;
    private NotificationsChangedReceiver? notificationsReceiver;
    private UninstallResultReceiver? uninstallReceiver;

    private const string UninstallResultAction = "com.storyworthtelling.focusphone.launcher.UNINSTALL_RESULT";
    private const string UninstallNoticeKey = "uninstall";

    // Keyed by package; the value is the normalized urgency TileNotificationListener broadcast
    // (0/1/2 — see its Urgency method), which ShelfArt.NotificationHue maps to a role hue. A key's
    // mere presence is still "has a notification" for HasNotification.
    private readonly Dictionary<string, BookmarkRole> notificationPriorities = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TapWatch.Attach(this);
        PaperTransitions.Apply(this);
        GoImmersive();
        RestoreUpdateTokenFromVault();
        SetContentView(BuildLayout());
        PolicyManager.ApplyCurrentPolicy(this);
        CrashReporter.ShowPendingCrash(this);
        if (!coverShownThisProcess)
        {
            coverShownThisProcess = true;
            ShowCoverLoad();
        }
    }

    // The frameless cover-load moment: the Flyleaf cover full-bleed over the shelf with a
    // time-aware voiced hello (hidden when there's no profile/key), then a gentle fade to the shelf.
    private void ShowCoverLoad()
    {
        var overlay = new FrameLayout(this);
        overlay.AddView(new CoverView(this), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var hello = PaperUi.Secondary(this, string.Empty);
        hello.Gravity = GravityFlags.Center;
        hello.SetTextColor(PaperTheme.Paper);
        hello.Visibility = ViewStates.Invisible;
        var pad = PaperTheme.Dp(this, DesignTokens.Space.X8);
        hello.SetPadding(pad, 0, pad, 0);
        overlay.AddView(hello, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Bottom | GravityFlags.CenterHorizontal,
            BottomMargin = PaperTheme.Dp(this, DesignTokens.Space.X8),
        });

        root!.AddView(overlay, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        greeting ??= new Codex.Ui.Voice.VoiceGreeting(voiceAccount.GetAccessToken, "Flyleaf");
        ComposeHelloThenFade(overlay, hello);
    }

    private async void ComposeHelloThenFade(FrameLayout overlay, TextView hello)
    {
        var timeContext = $"the owner just opened Flyleaf; it is {DateTime.Now:h:mm tt}";
        var compose = greeting!.ComposeAsync("welcome", timeContext, CancellationToken.None);
        var finished = await Task.WhenAny(compose, Task.Delay(2500));
        var message = ReferenceEquals(finished, compose) && compose.IsCompletedSuccessfully ? compose.Result : string.Empty;
        if (!string.IsNullOrWhiteSpace(message))
        {
            hello.Text = message;
            hello.Visibility = ViewStates.Visible;
            await Task.Delay(1400); // a beat to read it
        }
        else
        {
            await Task.Delay(700); // a brief cover beat, then reveal the shelf
        }
        if (IsFinishing)
        {
            return;
        }
        overlay.Animate()!.Alpha(0f)!.SetDuration(450)!
            .WithEndAction(new Java.Lang.Runnable(() => (overlay.Parent as ViewGroup)?.RemoveView(overlay)))!.Start();
    }

    private void GoImmersive()
    {
        Window!.DecorView.SystemUiFlags =
            SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen |
            SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen | SystemUiFlags.ImmersiveSticky;
    }

    private View BuildLayout()
    {
        art = new ShelfArt(this, () => notificationPriorities);
        shelf = new BookshelfView(this, art)
        {
            ItemTapped = OnItemTapped,
            ItemLongPressed = OnItemLongPressed,
        };

        var scroller = new ScrollView(this) { FillViewport = true };
        scroller.SetBackgroundColor(PaperTheme.WalnutDeep);
        scroller.AddView(shelf, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        root = new FrameLayout(this);
        root.AddView(scroller, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // A quiet paper prompt over the shelf until Flyleaf holds the home role (ROLE_HOME can't be
        // force-granted, so it opens the system Home-app picker). Vanishes once we're the default.
        setDefaultLauncherButton = PaperUi.SecondaryButton(this, "Set Flyleaf as your home app", () =>
            StartActivity(new Intent(Android.Provider.Settings.ActionHomeSettings)));
        setDefaultLauncherButton.Visibility = ViewStates.Gone;
        root.AddView(setDefaultLauncherButton, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Center,
        });

        // Same idea, for the restricted browser. Unlike ROLE_HOME, ROLE_BROWSER is directly
        // requestable, so this asks outright rather than only deep-linking to Settings — the owner
        // previously had to find the default-apps screen themselves (via a workaround) to make
        // links open in Flyleaf's contained browser instead of full Chrome.
        setDefaultBrowserButton = PaperUi.SecondaryButton(this, "Set Flyleaf as your browser", RequestBrowserRole);
        setDefaultBrowserButton.Visibility = ViewStates.Gone;
        root.AddView(setDefaultBrowserButton, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Center,
            TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X12),
        });

        // The way back into a phone call. The lockdown's disabled status bar takes the ongoing-call
        // chip and the shade with it, so leaving the call screen (or pressing Home while it rang)
        // left the call with nowhere to return to. Pinned while a call lasts, gone the moment it ends.
        callButton = PaperUi.PrimaryButton(this, string.Empty, OpenCurrentCall);
        callBar = PaperUi.BottomBar(this, callButton);
        callBar.SetBackgroundColor(PaperTheme.Paper);
        callBar.Visibility = ViewStates.Gone;
        root.AddView(callBar, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Bottom,
        });
        return root;
    }

    protected override void OnResume()
    {
        base.OnResume();
        // Home coming to the front while a locked hand-off runs is Home pressed: the way out (D288).
        LockedSessionController.OnHome(this);
        ReloadScene();
        UpdateClock();
        UpdateSetDefaultLauncherButton();
        UpdateSetDefaultBrowserButton();
        UpdateCallBar();
        timeTickReceiver = new TimeTickReceiver(UpdateClock);
        RegisterReceiver(timeTickReceiver, new IntentFilter(Intent.ActionTimeTick));
        notificationsReceiver = new NotificationsChangedReceiver(OnNotificationsChanged);
        RegisterReceiver(notificationsReceiver,
            new IntentFilter(TileNotificationListener.ActionNotificationsChanged), ReceiverFlags.NotExported);
        uninstallReceiver = new UninstallResultReceiver(this);
        RegisterReceiver(uninstallReceiver, new IntentFilter(UninstallResultAction), ReceiverFlags.NotExported);
    }

    protected override void OnPause()
    {
        base.OnPause();
        UnregisterReceiver(timeTickReceiver);
        UnregisterReceiver(notificationsReceiver);
        UnregisterReceiver(uninstallReceiver);
    }

    // Receives the device-owner uninstall result: on success/failure it rebuilds the shelf (so a removed
    // app disappears immediately); if the platform needs a user confirm (the silent path was refused), it
    // launches the confirm intent the installer handed back.
    private sealed class UninstallResultReceiver(HomeActivity activity) : BroadcastReceiver
    {
        private const int StatusPendingUserAction = -1; // PackageInstaller.STATUS_PENDING_USER_ACTION

        public override void OnReceive(Context? context, Intent? intent)
        {
            var status = intent?.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure)
                ?? (int)PackageInstallStatus.Failure;
            if (status == StatusPendingUserAction
                && intent?.GetParcelableExtra(Intent.ExtraIntent) is Intent confirm)
            {
                confirm.SetFlags(ActivityFlags.NewTask);
                activity.StartActivity(confirm);
                return;
            }
            var succeeded = status == (int)PackageInstallStatus.Success;
            var reason = intent?.GetStringExtra(PackageInstaller.ExtraStatusMessage) ?? $"status {status}";
            var package = intent?.GetStringExtra(PackageInstaller.ExtraPackageName) ?? "an app";
            if (PendingUninstalls.TryRemove(package, out var action))
            {
                if (succeeded)
                {
                    action.Succeeded();
                }
                else
                {
                    action.Failed($"refused by the platform ({reason})");
                }
            }
            if (!succeeded)
            {
                // A refused uninstall used to clear the notice and reload an unchanged shelf — exactly
                // "nothing happened". Record why, and say so on the dog-ear.
                new PolicyAuditStore(activity).Record(PolicyEventKind.UninstallFailed,
                    $"Uninstall of {package} was refused by the platform ({reason}).");
            }
            activity.RunOnUiThread(() =>
            {
                DogEar.SetNotice(activity, activity.root!, UninstallNoticeKey, succeeded
                    ? null
                    : new DogEar.Notice("Couldn't uninstall", reason, PaperTheme.ProblemMark));
                activity.ReloadScene();
            });
        }
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            GoImmersive(); // re-hide the bars after a transient system UI reveal
        }
    }

    private void ReloadScene()
    {
        var catalog = new CategoryStore(this).Load();
        var home = new HomeFeatureStore(this).Load();
        var scene = new ShelfSceneBuilder(this).Build(catalog, home);
        shelf!.SetScene(scene);
    }

    private void UpdateClock() => shelf?.SetTime(DateTime.Now.TimeOfDay);

    private void OnNotificationsChanged(IReadOnlyDictionary<string, BookmarkRole> priorities)
    {
        notificationPriorities.Clear();
        foreach (var (package, priority) in priorities)
        {
            notificationPriorities[package] = priority;
        }
        shelf?.RefreshBookmarks();
        UpdateCallBar();
    }

    private void UpdateCallBar()
    {
        var (kind, target) = CallScreen.Current;
        var label = CallNotice.ActionLabel(kind);
        if (label is null || target is null)
        {
            callBar!.Visibility = ViewStates.Gone;
            return;
        }
        callButton!.Text = label;
        callBar!.Visibility = ViewStates.Visible;
    }

    private void OpenCurrentCall()
    {
        var answering = TapWatch.Action(FlyleafKeyActions.AnswerCall, KeyActs.Local);
        if (CallScreen.Current.Target is not { } target || !CallScreen.Open(target))
        {
            // The call ended between the bar being drawn and the tap.
            answering.Failed("the call screen did not open");
            callBar!.Visibility = ViewStates.Gone;
            return;
        }
        answering.Succeeded();
    }

    // ---- Interactions ----

    private void OnItemTapped(PlacedItem item)
    {
        if (item.Key == BookshelfLayout.ClockKey)
        {
            return; // the clock is glanced, not tapped; long-press opens Settings
        }
        var intent = PackageManager!.GetLaunchIntentForPackage(item.Key);
        if (intent is not null)
        {
            StartActivity(intent);
        }
        else
        {
            Toast.MakeText(this, $"{art!.Label(item.Key)} isn't installed", ToastLength.Short)!.Show();
        }
    }

    private void OnItemLongPressed(PlacedItem item)
    {
        if (item.Key == BookshelfLayout.ClockKey)
        {
            StartActivity(new Intent(this, typeof(LauncherSettingsActivity)));
            return;
        }
        ShowAppMenu(item.Key);
    }

    private void ShowAppMenu(string package)
    {
        var label = art!.Label(package);
        var actions = new List<(string Label, Action OnClick)>();
        if (!AppTrayLayout.IsRepoApp(package))
        {
            actions.Add(("Move to shelf…", () => ShowShelfPicker(package, label)));
        }
        actions.Add(("Feature on home", () => ShowFeaturePicker(package, label)));
        actions.Add(("Block", () => BlockWithUndo(package, label)));
        if (package != PackageName)
        {
            // Every app can be removed except Flyleaf itself, the device owner holding the lockdown.
            actions.Add(("Uninstall", () => UninstallApp(package, label)));
        }
        ShowPaperMenu(label, actions);
    }

    // Device-owner SILENT uninstall (owner's call): as device owner, Flyleaf can remove an app outright via
    // the PackageInstaller, no system dialog. The long-press action card is the deliberate confirmation. The
    // result broadcast rebuilds the shelf so the app disappears at once; if the platform can't do it silently
    // (not device owner for this app), it hands back a confirm intent we launch instead of failing.
    //
    // An "Uninstalling…" dog-ear notice goes up the moment this is invoked and is cleared by the result
    // receiver: the silent path is asynchronous with nothing else on screen changing meanwhile, so without
    // it a tap reads as "nothing happened" for however long the broadcast takes to arrive — or forever, if
    // it never does.
    // The uninstall's key action, until the platform's result broadcast answers it (by package).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Storyworthtelling.Diagnostics.KeyAction> PendingUninstalls = new();

    private void UninstallApp(string package, string label)
    {
        PendingUninstalls[package] = TapWatch.Action(FlyleafKeyActions.Uninstall, TimeSpan.FromMinutes(2));
        DogEar.SetNotice(this, root!, UninstallNoticeKey,
            new DogEar.Notice("Uninstalling…", label, PaperTheme.ProgressMark));

        if (!PolicyManager.IsDeviceOwner(this))
        {
            // Not device owner (e.g. a dev build) — the silent path is refused outright, so go
            // straight to the visible system uninstaller rather than a call that will quietly fail.
            LaunchVisibleUninstaller(package);
            return;
        }

        try
        {
            var installer = PackageManager!.PackageInstaller;
            var intent = new Intent(UninstallResultAction).SetPackage(PackageName);
            var pending = PendingIntent.GetBroadcast(this, package.GetHashCode(), intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;
            installer.Uninstall(package, pending.IntentSender);
            return;
        }
        catch (Java.Lang.Exception e)
        {
            // Silent path unavailable — fall back to Android's own uninstaller UI. Unlike every
            // other PolicyManager failure path, this one used to be swallowed with no trace, so a
            // failure here and a silently-dropped fallback (Intent.ActionUninstallPackage without
            // REQUEST_DELETE_PACKAGES, per ManifestPermissions.cs) looked identical on-device: an
            // uninstall tap that did nothing. Logging which branch actually ran distinguishes them.
            new PolicyAuditStore(this).Record(PolicyEventKind.UninstallFailed,
                $"Silent uninstall of {package} failed ({e.GetType().Name}: {e.Message}); falling back to the visible uninstaller.");
        }

        LaunchVisibleUninstaller(package);
    }

    private void LaunchVisibleUninstaller(string package)
    {
        var uri = Android.Net.Uri.Parse("package:" + package);
        try
        {
            StartActivity(new Intent(Intent.ActionUninstallPackage, uri).SetFlags(ActivityFlags.NewTask));
        }
        catch (Android.Content.ActivityNotFoundException)
        {
            try
            {
                StartActivity(new Intent(Intent.ActionDelete, uri).SetFlags(ActivityFlags.NewTask));
            }
            catch (Android.Content.ActivityNotFoundException)
            {
                // No uninstaller available (unusual); nothing more we can do from the launcher.
                DogEar.SetNotice(this, root!, UninstallNoticeKey,
                    new DogEar.Notice("Couldn't uninstall", "No uninstaller is available on this device.", PaperTheme.ProblemMark));
            }
        }
    }

    private void ShowShelfPicker(string package, string label)
    {
        var catalog = new CategoryStore(this).Load();
        var store = new AppFolderStore(this);
        var actions = catalog.Categories
            .Select(category => (category.Name, (Action)(() => KeyActs.Run(
                TapWatch.Action(FlyleafKeyActions.MoveToShelf, KeyActs.Local), () => { store.SetFolder(package, category.Id); ReloadScene(); }))))
            .ToList();
        actions.Add(("None (loose)", () => KeyActs.Run(
            TapWatch.Action(FlyleafKeyActions.MoveToShelf, KeyActs.Local), () => { store.ClearFolder(package); ReloadScene(); })));
        ShowPaperMenu($"Move {label} to…", actions);
    }

    private void ShowFeaturePicker(string package, string label)
    {
        var store = new HomeFeatureStore(this);
        var home = store.Load();
        var hidden = new HiddenAppsStore(this).Load();
        var actions = home.Packages
            .Select(featured => (art!.Label(featured), (Action)(() => KeyActs.Run(
                TapWatch.Action(FlyleafKeyActions.Feature, KeyActs.Local), () =>
            {
                store.Save(home.Replace(featured, package, hidden));
                ReloadScene();
            }))))
            .ToList();
        ShowPaperMenu($"Feature {label} — replace which book?", actions);
    }

    private void BlockWithUndo(string package, string label)
    {
        KeyActs.Run(TapWatch.Action(FlyleafKeyActions.Block, KeyActs.Local), () => SetBlocked(package, blocked: true));
        // A folded page-corner (book metaphor), not a toast/bar: tap the dog-ear to turn back the block.
        DogEar.Show(this, root!, $"Undo blocking {label}", onUndo: () => SetBlocked(package, blocked: false));
    }

    private void SetBlocked(string package, bool blocked)
    {
        var store = new BlocklistStore(this);
        var categorizer = new AppCategorizer(store.Load());
        if (blocked)
        {
            categorizer.Block(package);
        }
        else
        {
            categorizer.Unblock(package);
        }
        store.Save(categorizer.Export());
        new PolicyAuditStore(this).Record(
            blocked ? Codex.Domain.Policy.PolicyEventKind.AppBlocked : Codex.Domain.Policy.PolicyEventKind.AppUnblocked,
            $"{(blocked ? "Blocked" : "Unblocked")} {package}");
        PolicyManager.ApplyCurrentPolicy(this);
        ReloadScene();
    }

    // A Paper action sheet: a Serif title over left-aligned quiet buttons on a rounded paper card.
    private void ShowPaperMenu(string title, IReadOnlyList<(string Label, Action OnClick)> actions)
    {
        var dialog = new Dialog(this);
        dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var pad = PaperTheme.Dp(this, DesignTokens.Space.X6);
        column.SetPadding(pad, pad, pad, pad);
        column.AddView(PaperUi.Heading(this, title));

        foreach (var action in actions)
        {
            var button = PaperUi.QuietButton(this, action.Label, () =>
            {
                dialog.Dismiss();
                action.OnClick();
            });
            button.Gravity = GravityFlags.CenterVertical | GravityFlags.Start;
            column.AddView(button, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        dialog.SetContentView(column);
        dialog.Window?.SetBackgroundDrawable(PaperUi.Fill(this, PaperTheme.Paper, DesignTokens.Shape.RadiusCard));
        dialog.Show();
    }

    private void UpdateSetDefaultLauncherButton()
    {
        if (setDefaultLauncherButton is null)
        {
            return;
        }
        var roleManager = GetSystemService(Context.RoleService) as Android.App.Roles.RoleManager;
        var isDefault = roleManager?.IsRoleHeld(Android.App.Roles.RoleManager.RoleHome) ?? false;
        setDefaultLauncherButton.Visibility = isDefault ? ViewStates.Gone : ViewStates.Visible;
    }

    private void UpdateSetDefaultBrowserButton()
    {
        if (setDefaultBrowserButton is null)
        {
            return;
        }
        var roleManager = GetSystemService(Context.RoleService) as Android.App.Roles.RoleManager;
        var isDefault = roleManager?.IsRoleHeld(Android.App.Roles.RoleManager.RoleBrowser) ?? false;
        setDefaultBrowserButton.Visibility = isDefault ? ViewStates.Gone : ViewStates.Visible;
    }

    private void RequestBrowserRole()
    {
        var roleManager = GetSystemService(Context.RoleService) as Android.App.Roles.RoleManager;
        if (roleManager is null || !roleManager.IsRoleAvailable(Android.App.Roles.RoleManager.RoleBrowser!))
        {
            StartActivity(new Intent(Android.Provider.Settings.ActionManageDefaultAppsSettings));
            return;
        }
        StartActivityForResult(
            roleManager.CreateRequestRoleIntent(Android.App.Roles.RoleManager.RoleBrowser!), RequestBrowserRoleCode);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == RequestBrowserRoleCode)
        {
            UpdateSetDefaultBrowserButton();
        }
    }

    // After a reinstall or the cutover's factory reset, refill the GitHub update token from the
    // encrypted Drive vault so the self-update channel keeps working. Runs only when it's missing.
    private void RestoreUpdateTokenFromVault() => Task.Run(async () =>
    {
        try
        {
            var settings = new UpdateSettingsStore(this);
            if (!string.IsNullOrEmpty(settings.GitHubToken))
            {
                return;
            }
            var access = new Codex.Ui.Voice.SecretsVaultAccess(voiceAccount.GetAccessToken, "Flyleaf");
            var result = await access.Service.LoadAsync(CancellationToken.None);
            if (result.Status == SecretsVaultStatus.Loaded)
            {
                Codex.Ui.Voice.SharedDriveFileIds.SeedFrom(result.Vault.DriveFileIds);
                if (!string.IsNullOrEmpty(result.Vault.GitHubUpdateToken))
                {
                    settings.GitHubToken = result.Vault.GitHubUpdateToken;
                }
            }
        }
        catch
        {
            // Best-effort; admin mode always re-enters the token manually.
        }
    });

    // ---- The art the shelf paints through ----

    private sealed class ShelfArt(Context context, Func<IReadOnlyDictionary<string, BookmarkRole>> notifications) : IShelfArt
    {
        private readonly PackageManager packageManager = context.PackageManager!;
        private readonly Android.Content.Res.Resources resources = context.Resources!;
        private readonly Dictionary<string, Drawable?> icons = [];
        private readonly Dictionary<string, string> labels = [];
        private readonly Dictionary<string, Android.Graphics.Bitmap?> covers = [];

        // The WINDOW illustration for a book, keyed by package: the painting that fills the bookplate's
        // illustration window (not the whole cover). Every home book uses the ex-libris bookplate — the
        // app's identity cloth fills the plate, this illustration fills the window — so the six read as one
        // set. Google apps carry our bespoke illustrations; apps without one fall back to the launcher icon.
        private static readonly Dictionary<string, int> CoverResources = new()
        {
            // The heads' full-bleed source paintings (the same art the launcher icons were cropped from),
            // filling the bookplate window. Every featured package needs an entry: without one the book
            // silently falls back to the launcher icon, which looks like a design choice rather than a
            // gap — guarded by FrozenInvariantsTests so the next head cannot ship that way.
            [HomeFeatureSet.CommunicationPackage] = Resource.Drawable.cover_post,
            [HomeFeatureSet.CameraPackage] = Resource.Drawable.cover_plate,
            [HomeFeatureSet.AudioPackage] = Resource.Drawable.cover_score,
            [HomeFeatureSet.MapsPackage] = Resource.Drawable.cover_maps,

            // The owner's other repo apps, which the tray draws cover-out on the same shelf. They
            // are not in HomeFeatureSet, which is why Foreword shipped wearing a circle: the
            // featured-six guard could not see it, and the fallback centres the LAUNCHER icon,
            // which Android masks round. Folio's painting shipped here as cover_folio for a whole
            // phase with no entry, so its book showed the launcher icon (P12, D277). Index's,
            // Folio's and Pulp's covers are window only: the shelf draws the rule, the title band
            // and the brackets around them.
            [PolicyContext.ForewordPackage] = Resource.Drawable.cover_foreword,
            [PolicyContext.IndexPackage] = Resource.Drawable.cover_index,
            [PolicyContext.FolioPackage] = Resource.Drawable.cover_folio,
            [PolicyContext.PulpPackage] = Resource.Drawable.cover_pulp,
            [PolicyContext.ConstitutionPackage] = Resource.Drawable.cover_constitution,
            [HomeFeatureSet.DrivePackage] = Resource.Drawable.cover_drive,
            [HomeFeatureSet.GoogleHomePackage] = Resource.Drawable.cover_googlehome,
        };

        public Android.Graphics.Bitmap? Cover(string packageKey)
        {
            if (!covers.TryGetValue(packageKey, out var cover))
            {
                covers[packageKey] = cover = CoverResources.TryGetValue(packageKey, out var resId)
                    ? Android.Graphics.BitmapFactory.DecodeResource(resources, resId)
                    : null;
            }
            return cover;
        }

        public Drawable? Icon(string packageKey)
        {
            if (!icons.TryGetValue(packageKey, out var icon))
            {
                icons[packageKey] = icon = TryLoad(packageKey);
            }
            return icon;
        }

        public string Label(string packageKey)
        {
            if (!labels.TryGetValue(packageKey, out var label))
            {
                labels[packageKey] = label = TryLabel(packageKey);
            }
            return label;
        }

        public int Cloth(string packageKey) =>
            Android.Graphics.Color.ParseColor(BookClothPalette.ForPackage(packageKey)).ToArgb();

        public bool MarksInvertToInk(string packageKey) =>
            BookClothPalette.MarksInvertToInk(BookClothPalette.ForPackage(packageKey));

        public bool HasNotification(string packageKey) => notifications().ContainsKey(packageKey);

        // Priority, not cloth: DESIGN.md §9's role hues, so an urgent notification reads as
        // urgent regardless of which book's binding it is pinned to. The role was decided by
        // NotificationBookmark.Decide when the broadcast arrived: urgency collapsed to 0/1/2 by
        // TileNotificationListener, unless Constitution's own role extra says otherwise (P13 D91).
        public int NotificationHue(string packageKey) =>
            notifications().TryGetValue(packageKey, out var role)
                ? role switch
                {
                    BookmarkRole.Problem => PaperTheme.ProblemMark.ToArgb(),
                    BookmarkRole.Reference => PaperTheme.ReferenceMark.ToArgb(),
                    BookmarkRole.Resolved => PaperTheme.ResolvedMark.ToArgb(),
                    _ => PaperTheme.ProgressMark.ToArgb(),
                }
                : PaperTheme.ProgressMark.ToArgb();

        private Drawable? TryLoad(string package)
        {
            try { return packageManager.GetApplicationIcon(package); }
            catch (PackageManager.NameNotFoundException) { return null; }
        }

        private string TryLabel(string package)
        {
            try { return packageManager.GetApplicationLabel(packageManager.GetApplicationInfo(package, 0)); }
            catch (PackageManager.NameNotFoundException) { return ShortName(package); }
        }

        private static string ShortName(string package)
        {
            var last = package.Split('.')[^1];
            return last.Length == 0 ? package : char.ToUpperInvariant(last[0]) + last[1..];
        }
    }

    private class TimeTickReceiver(Action onTick) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => onTick();
    }

    private class NotificationsChangedReceiver(Action<IReadOnlyDictionary<string, BookmarkRole>> onChanged) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            var packages = intent?.GetStringArrayExtra(TileNotificationListener.ExtraPackages) ?? [];
            var priorities = intent?.GetIntArrayExtra(TileNotificationListener.ExtraPriorities) ?? [];
            var roles = intent?.GetStringArrayExtra(TileNotificationListener.ExtraRoles) ?? [];
            var byPackage = new Dictionary<string, BookmarkRole>();
            for (var i = 0; i < packages.Length; i++)
            {
                // Priorities is parallel to packages (TileNotificationListener.BroadcastActivePackages);
                // a short array here would only mean an old build sent the intent — default to 1
                // (the neutral "default" urgency) rather than throw.
                var urgency = i < priorities.Length ? priorities[i] : 1;
                var role = i < roles.Length ? roles[i] : null;
                byPackage[packages[i]] = NotificationBookmark.Decide(packages[i], urgency, role);
            }
            onChanged(byPackage);
        }
    }
}
