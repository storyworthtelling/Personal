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

    // Kept for the vault-backed GitHub update-token restore below (the voiced hello itself moved
    // to the load screen). Best-effort: no-ops until a Google account is signed in.
    private readonly Codex.Ui.Voice.VoiceGoogleAccount voiceAccount = new();

    private TimeTickReceiver? timeTickReceiver;
    private NotificationsChangedReceiver? notificationsReceiver;
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
    }

    private void GoImmersive()
    {
        Window!.DecorView.SystemUiVisibility = (StatusBarVisibility)(
            SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen |
            SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen | SystemUiFlags.ImmersiveSticky);
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
    }

    protected override void OnPause()
    {
        base.OnPause();
        UnregisterReceiver(timeTickReceiver);
        UnregisterReceiver(notificationsReceiver);
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
        actions.Add(("Uninstall", () => StartActivity(
            new Intent(Intent.ActionDelete, Android.Net.Uri.Parse("package:" + package)))));
        ShowPaperMenu(label, actions);
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
        UndoBar.Show(this, root!, $"Blocked {label}.", onUndo: () => SetBlocked(package, blocked: false));
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
        private readonly Dictionary<string, Drawable?> icons = [];
        private readonly Dictionary<string, string> labels = [];

        public Android.Graphics.Bitmap? Cover(string packageKey) => null; // bespoke painted covers land in the cover pass

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
