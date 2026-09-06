using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Apps;
using Codex.Domain.Design;
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
    private readonly HashSet<string> notifiedPackages = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
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
        art = new ShelfArt(this, () => notifiedPackages);
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
        return root;
    }

    protected override void OnResume()
    {
        base.OnResume();
        ReloadScene();
        UpdateClock();
        UpdateSetDefaultLauncherButton();
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
            activity.RunOnUiThread(activity.ReloadScene);
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

    private void OnNotificationsChanged(IReadOnlySet<string> packages)
    {
        notifiedPackages.Clear();
        notifiedPackages.UnionWith(packages);
        shelf?.RefreshBookmarks();
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
        if (!IsRepoApp(package))
        {
            actions.Add(("Move to shelf…", () => ShowShelfPicker(package, label)));
        }
        actions.Add(("Feature on home", () => ShowFeaturePicker(package, label)));
        actions.Add(("Block", () => BlockWithUndo(package, label)));
        actions.Add(("Uninstall", () => UninstallApp(package)));
        ShowPaperMenu(label, actions);
    }

    // Device-owner SILENT uninstall (owner's call): as device owner, Flyleaf can remove an app outright via
    // the PackageInstaller, no system dialog. The long-press action card is the deliberate confirmation. The
    // result broadcast rebuilds the shelf so the app disappears at once; if the platform can't do it silently
    // (not device owner for this app), it hands back a confirm intent we launch instead of failing.
    private void UninstallApp(string package)
    {
        try
        {
            var installer = PackageManager!.PackageInstaller;
            var intent = new Intent(UninstallResultAction).SetPackage(PackageName);
            var pending = PendingIntent.GetBroadcast(this, package.GetHashCode(), intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable)!;
            installer.Uninstall(package, pending.IntentSender);
            return;
        }
        catch (Java.Lang.Exception)
        {
            // Silent path unavailable — fall back to Android's own uninstaller UI.
        }

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
            }
        }
    }

    private void ShowShelfPicker(string package, string label)
    {
        var catalog = new CategoryStore(this).Load();
        var store = new AppFolderStore(this);
        var actions = catalog.Categories
            .Select(category => (category.Name, (Action)(() => { store.SetFolder(package, category.Id); ReloadScene(); })))
            .ToList();
        actions.Add(("None (loose)", () => { store.ClearFolder(package); ReloadScene(); }));
        ShowPaperMenu($"Move {label} to…", actions);
    }

    private void ShowFeaturePicker(string package, string label)
    {
        var store = new HomeFeatureStore(this);
        var home = store.Load();
        var actions = home.Packages
            .Select(featured => (art!.Label(featured), (Action)(() =>
            {
                store.Save(home.Replace(featured, package));
                ReloadScene();
            })))
            .ToList();
        ShowPaperMenu($"Feature {label} — replace which book?", actions);
    }

    private void BlockWithUndo(string package, string label)
    {
        SetBlocked(package, blocked: true);
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

    private static bool IsRepoApp(string package) =>
        package.StartsWith(Codex.Domain.Policy.PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal)
        && !Codex.Domain.Policy.PolicyContext.FocusPhonePackages.Contains(package);

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

    private sealed class ShelfArt(Context context, Func<IReadOnlySet<string>> notifications) : IShelfArt
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

        public bool HasNotification(string packageKey) => notifications().Contains(packageKey);

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

    private class NotificationsChangedReceiver(Action<IReadOnlySet<string>> onChanged) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            var packages = intent?.GetStringArrayExtra(TileNotificationListener.ExtraPackages) ?? [];
            onChanged(packages.ToHashSet());
        }
    }
}
